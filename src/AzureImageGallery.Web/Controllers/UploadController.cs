// ----------------------------------------------------------------------------------------------------
// <copyright file="UploadController.cs" company="Microsoft">
//     Copyright &#169; Microsoft Corporation. All rights reserved.
// </copyright>
// ----------------------------------------------------------------------------------------------------

namespace AzureImageGallery.Web.Controllers
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Azure.Storage;
    using Azure.Storage.Blobs;
    using Azure.Storage.Blobs.Models;
    using AzureImageGallery.Data;
    using AzureImageGallery.Web.Models;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Configuration;

    public static class ConfigurationExtensions
    {
        public static StorageTransferOptions GetStorageTransferOptions(this IConfiguration config)
        {
            var section = config?.GetSection("StorageTransferOptions");
            if (section is null)
                return new StorageTransferOptions();
            return new StorageTransferOptions
            {
                MaximumTransferSize = long.TryParse(section["MaximumTransferSize"], out var maximumTransferSize)
                    ? maximumTransferSize
                    : null,
                MaximumConcurrency = int.TryParse(section["MaximumConcurrency"], out var maximumConcurrency)
                    ? maximumConcurrency
                    : null,
                InitialTransferSize = long.TryParse(section["InitialTransferSize"], out var initialTransferSize)
                    ? initialTransferSize
                    : null
            };
        }
    }

    public class UploadController : Controller
    {
        public UploadController(IConfiguration config, IImage imageService, BlobClientOptions options)
        {
            Configuration = config;
            ImageService = imageService;
            AzureConnectionString = Configuration.GetConnectionString("AzureStorageConnectionString");
            BlobClientOptions = options;
            StorageTransferOptions = Configuration.GetStorageTransferOptions();
        }

        private string AzureConnectionString { get; }

        private BlobClientOptions BlobClientOptions { get; }

        private IConfiguration Configuration { get; }

        private IImage ImageService { get; }

        private StorageTransferOptions StorageTransferOptions { get; }

        //[Authorize]
        public IActionResult Index()
        {
            var vm = new UploadImageModel();

            return View(vm);
        }

        [HttpPost]
        public async Task<IActionResult> Upload(string title, string tags)
        {
            //try
            //{
                var formCollection = await Request.ReadFormAsync();
                var image = formCollection.Files.First();

                if (image.Length > 0)
                {
                    var container = new BlobContainerClient(AzureConnectionString, "images", BlobClientOptions);
                    var createResponse = await container.CreateIfNotExistsAsync();

                    if (createResponse is not null && createResponse.GetRawResponse().Status == 201)
                    {
                        await container.SetAccessPolicyAsync(PublicAccessType.Blob);
                    }

                    var blob = container.GetBlobClient(image.FileName.Trim('"'));
                    await blob.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots);
                    using (var fileStream = image.OpenReadStream())
                    {
                        await blob.UploadAsync(fileStream, new BlobHttpHeaders { ContentType = image.ContentType }, transferOptions: StorageTransferOptions);
                    }

                    await ImageService.SetImage(title, tags, blob.Uri);

                    return RedirectToAction(nameof(GalleryController.Index), "Gallery", blob.Uri.ToString());
                }

                return BadRequest();
            //}
            //catch (Exception ex)
            //{
            //    return StatusCode(500, $"Internal server error: {ex}");
            //}
        }
    }
}
