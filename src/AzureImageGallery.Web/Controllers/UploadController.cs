using System;
using System.Linq;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using AzureImageGallery.Data;
using AzureImageGallery.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace AzureImageGallery.Web.Controllers
{
    public class UploadController : Controller
    {
        private string AzureConnectionString { get; }
        private IConfiguration Configuration { get; }
        private IImage ImageService { get; }

        public UploadController(IConfiguration config, IImage imageService)
        {
            Configuration = config;
            ImageService = imageService;
            AzureConnectionString = Configuration.GetConnectionString("AzureStorageConnectionString");
        }

        //[Authorize]
        public IActionResult Index()
        {
            var vm = new UploadImageModel();

            return View(vm);
        }

        [HttpPost]
        public async Task<IActionResult> Upload(string title, string tags)
        {
            try
            {
                var formCollection = await Request.ReadFormAsync();
                var image = formCollection.Files.First();

                if(image.Length > 0)
                {
                    var container = new BlobContainerClient(AzureConnectionString, "images");
                    var createResponse = await container.CreateIfNotExistsAsync();

                    if(createResponse is not null && createResponse.GetRawResponse().Status == 201)
                    {
                        await container.SetAccessPolicyAsync(PublicAccessType.Blob);
                    }

                    var blob = container.GetBlobClient(image.FileName.Trim('"'));
                    await blob.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots);

                    using(var fileStream = image.OpenReadStream())
                    {
                        await blob.UploadAsync(fileStream, new BlobHttpHeaders { ContentType = image.ContentType });
                    }

                    await ImageService.SetImage(title, tags, blob.Uri);

                    return RedirectToAction(nameof(GalleryController.Index), "Gallery", blob.Uri.ToString());
                }

                return BadRequest();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex}");
            }
        }
    }
}
