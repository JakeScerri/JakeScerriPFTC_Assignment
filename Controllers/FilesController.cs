using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JakeScerriPFTC_Assignment.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace JakeScerriPFTC_Assignment.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FilesController : ControllerBase
    {
        private readonly StorageService _storageService;

        public FilesController(StorageService storageService)
        {
            _storageService = storageService;
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadFiles(List<IFormFile> files)
        {
            try
            {
                if (files == null || files.Count == 0)
                {
                    return BadRequest("No files were uploaded.");
                }

                // For testing, we'll just use a fixed email
                var userEmail = "test@example.com";
                
                var uploadedUrls = await _storageService.UploadFilesAsync(files, userEmail);
                
                return Ok(new { FileUrls = uploadedUrls });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}