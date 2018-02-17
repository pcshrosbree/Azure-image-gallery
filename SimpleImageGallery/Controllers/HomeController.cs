﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SimpleImageGallery.Models;
using SimpleImageGallery.Services;
using SimpleImageGallery.Data;

namespace SimpleImageGallery.Controllers
{
    public class HomeController : Controller
    {
        private readonly IImage _imageService;

        public HomeController(IImage imageService)
        {
            _imageService = imageService; //implements the interface of IImage - add to scope service in startup
        }

        public IActionResult Index()
        {
            var imageList = _imageService.GetAll();

            var model = new GalleryIndexModel()
            {
                //mock images to be removed after search query is fully implemented
                Images = imageList,
                SearchQuery = ""
            };
            return View(model);
        }

        public IActionResult About()
        {
            ViewData["Message"] = "Your application description page.";

            return View();
        }

        public IActionResult Contact()
        {
            ViewData["Message"] = "Your contact page.";

            return View();
        }

        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
