// ----------------------------------------------------------------------------------------------------
// <copyright file="FaultInjectionTransport.cs" company="Microsoft">
//     Copyright &#169; Microsoft Corporation. All rights reserved.
// </copyright>
// ----------------------------------------------------------------------------------------------------

namespace AzureImageGallery.Web
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading.Tasks;
    using Azure;
    using Azure.Core;
    using Azure.Core.Pipeline;
    using Microsoft.Extensions.Logging;

    public class FaultInjectionTransport : HttpPipelineTransport
    {
        public FaultInjectionTransport(HttpPipelineTransport transport, double throttlingRate, ILogger<FaultInjectionTransport> logger)
        {
            Transport = transport;
            Logger = logger;
            ThrottlingRate = throttlingRate;
        }

        private ILogger<FaultInjectionTransport> Logger { get; }

        private HttpPipelineTransport Transport { get; }

        public override Request CreateRequest() => Transport.CreateRequest();

        public override void Process(HttpMessage message)
        {
            if (!InterceptRequest(message))
                Transport.Process(message);
        }

        public override ValueTask ProcessAsync(HttpMessage message)
            => InterceptRequest(message)
                ? ValueTask.CompletedTask
                : Transport.ProcessAsync(message);

        private const string StorageErrorTemplate =
            @"<?xml version=""1.0"" encoding=""utf-8""?>
<Error>
  <Code>{0}</Code>
  <Message>{1}</Message>
</Error>
";

        public struct StorageError
        {
            public string ErrorCode { get; init; }

            public HttpStatusCode StatusCode { get; init; }

            public string UserMessage { get; init; }

            public string ReasonPhrase { get; init; }
        }

        private double ThrottlingRate { get; }

        protected bool InterceptRequest(HttpMessage message)
        {
            var request = message.Request;
            if (IsBlobPut(request) && ShouldThrottle())
            {
                Logger.LogInformation("Throttling request {0}", request.ClientRequestId);
                var requestHeaders = request.Headers;
                var error = new StorageError
                {
                    ErrorCode = "ServerBusy",
                    ReasonPhrase = "Service Unavailable",
                    StatusCode = HttpStatusCode.ServiceUnavailable,
                    UserMessage = "Operations per second is over the account limit."
                };
                var content = string.Format(StorageErrorTemplate, error.ErrorCode, error.UserMessage);
                var contentStream = GetStreamFromString(content);
                var httpMethod = new HttpMethod(request.Method.Method);
                var requestUri = request.Uri.ToUri();
                var httpRequestMessage = new HttpRequestMessage(httpMethod, requestUri);
                var httpResponseMessage = new HttpResponseMessage(error.StatusCode)
                {
                    RequestMessage = httpRequestMessage,
                    Content = new StringContent(content),
                    ReasonPhrase = error.ReasonPhrase
                };
                var responseHeaders = httpResponseMessage.Headers;
                responseHeaders.Add("x-ms-error-code", error.ErrorCode);
                if (ReturnClientRequestId(requestHeaders))
                    responseHeaders.Add("x-ms-return-client-request-id", request.ClientRequestId);
                message.Response = new FaultPipelineResponse(request.ClientRequestId, httpResponseMessage, contentStream);
                return true;
            }

            return false;

            static bool ReturnClientRequestId(RequestHeaders requestHeaders) 
                => requestHeaders.TryGetValue("x-ms-return-client-request-id", out var returnClientRequestIdText)
                && bool.TryParse(returnClientRequestIdText, out var returnClientRequestId)
                && returnClientRequestId;

            static Stream GetStreamFromString(string s)
            {
                var stream = new MemoryStream();
                var writer = new StreamWriter(stream);
                writer.Write(s);
                writer.Flush();
                stream.Position = 0;
                return stream;
            }

            bool ShouldThrottle() 
                => new Random(DateTime.Now.Millisecond).NextDouble() < ThrottlingRate;
        }

        private static bool IsBlobPut(Request request) 
            => request.Method == RequestMethod.Put 
               && !request.Uri.Query.Contains("restype=container");

        private Stopwatch ThrottlingTimer { get; } = new();

        private Stopwatch AvailableTimer { get; } = new();

        internal static bool TryGetHeader(HttpHeaders headers, HttpContent? content, string name, [NotNullWhen(true)] out string? value)
        {
            if (TryGetHeader(headers, content, name, out IEnumerable<string>? values))
            {
                value = JoinHeaderValues(values);
                return true;
            }

            value = null;
            return false;
        }

        internal static bool TryGetHeader(HttpHeaders headers, HttpContent? content, string name, [NotNullWhen(true)] out IEnumerable<string>? values)
        {
            return headers.TryGetValues(name, out values) ||
                   content != null &&
                   content.Headers.TryGetValues(name, out values);
        }

        private static string JoinHeaderValues(IEnumerable<string> values)
        {
            return string.Join(",", values);
        }

        internal static IEnumerable<HttpHeader> GetHeaders(HttpHeaders headers, HttpContent? content)
        {
            foreach (KeyValuePair<string, IEnumerable<string>> header in headers)
            {
                yield return new HttpHeader(header.Key, JoinHeaderValues(header.Value));
            }

            if (content != null)
            {
                foreach (KeyValuePair<string, IEnumerable<string>> header in content.Headers)
                {
                    yield return new HttpHeader(header.Key, JoinHeaderValues(header.Value));
                }
            }
        }

        internal static bool RemoveHeader(HttpHeaders headers, HttpContent? content, string name)
        {
            // .Remove throws on invalid header name so use TryGet here to check
            if (headers.TryGetValues(name, out _) && headers.Remove(name))
            {
                return true;
            }

            return content?.Headers.TryGetValues(name, out _) == true && content.Headers.Remove(name);
        }

        internal static bool ContainsHeader(HttpHeaders headers, HttpContent? content, string name)
        {
            // .Contains throws on invalid header name so use TryGet here
            if (headers.TryGetValues(name, out _))
            {
                return true;
            }

            return content?.Headers.TryGetValues(name, out _) == true;
        }

        internal static void CopyHeaders(HttpHeaders from, HttpHeaders to)
        {
            foreach (KeyValuePair<string, IEnumerable<string>> header in from)
            {
                if (!to.TryAddWithoutValidation(header.Key, header.Value))
                {
                    throw new InvalidOperationException($"Unable to add header {header} to header collection.");
                }
            }
        }
    }

    public sealed class FaultPipelineResponse : Response
    {
        private readonly HttpResponseMessage _responseMessage;

        private readonly HttpContent _responseContent;

#pragma warning disable CA2213 // Content stream is intentionally not disposed
        private Stream _contentStream;
#pragma warning restore CA2213

        public FaultPipelineResponse(string requestId, HttpResponseMessage responseMessage, Stream contentStream)
        {
            ClientRequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
            _responseMessage = responseMessage ?? throw new ArgumentNullException(nameof(responseMessage));
            _contentStream = contentStream;
            _responseContent = _responseMessage.Content;
        }

        public override int Status => (int)_responseMessage.StatusCode;

        public override string ReasonPhrase => _responseMessage.ReasonPhrase ?? string.Empty;

        public override Stream ContentStream
        {
            get => _contentStream;

            set
            {
                // Make sure we don't dispose the content if the stream was replaced
                _responseMessage.Content = null;

                _contentStream = value;
            }
        }

        public override string ClientRequestId { get; set; }

        protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string? value)
            => FaultInjectionTransport.TryGetHeader(_responseMessage.Headers, _responseContent, name, out value);

        protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string> values)
            => FaultInjectionTransport.TryGetHeader(_responseMessage.Headers, _responseContent, name, out values);

        protected override bool ContainsHeader(string name) => FaultInjectionTransport.ContainsHeader(_responseMessage.Headers, _responseContent, name);

        protected override IEnumerable<HttpHeader> EnumerateHeaders() => GetHeaders(_responseMessage.Headers, _responseContent);

        public override void Dispose() => _responseMessage?.Dispose();

        public override string ToString() => _responseMessage.ToString();

        internal static IEnumerable<HttpHeader> GetHeaders(HttpHeaders headers, HttpContent? content)
        {
            foreach (KeyValuePair<string, IEnumerable<string>> header in headers)
            {
                yield return new HttpHeader(header.Key, JoinHeaderValues(header.Value));
            }

            if (content is not null)
            {
                foreach (KeyValuePair<string, IEnumerable<string>> header in content.Headers)
                {
                    yield return new HttpHeader(header.Key, JoinHeaderValues(header.Value));
                }
            }
        }

        private static string JoinHeaderValues(IEnumerable<string> values)
        {
            return string.Join(",", values);
        }
    }
}
