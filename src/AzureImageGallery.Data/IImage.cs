// ----------------------------------------------------------------------------------------------------
// <copyright file="IImage.cs" company="Microsoft">
//     Copyright &#169; Microsoft Corporation. All rights reserved.
// </copyright>
// ----------------------------------------------------------------------------------------------------

namespace AzureImageGallery.Data
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using AzureImageGallery.Data.Models;

    public interface IImage
    {
        void DeleteImage(int id);

        IEnumerable<GalleryImage> GetAll();

        IEnumerable<GalleryImage> GetAllWithPaging(int pageNumber, int pageSize);

        GalleryImage GetById(int id);

        IEnumerable<GalleryImage> GetWithTag(string tag);

        List<ImageTag> ParseTags(string tags);

        IEnumerable<GalleryImage> Range(int skip, int take);

        Task SetImage(string title, string tags, Uri uri);

        void UpdateImage(GalleryImage changeImage);
    }
}
