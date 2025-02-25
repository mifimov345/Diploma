using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Threading.Tasks;

namespace FileService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FileController : ControllerBase
    {
        // POST: api/file/upload
        [HttpPost("upload")]
        public async Task<IActionResult> UploadFile([FromForm] FileUploadModel model)
        {
            if (model.File == null || model.File.Length == 0)
                return BadRequest("No file uploaded.");

            // Папка для сохранения файлов (на сервере)
            var uploads = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
            if (!Directory.Exists(uploads))
                Directory.CreateDirectory(uploads);

            var filePath = Path.Combine(uploads, model.File.FileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await model.File.CopyToAsync(stream);
            }
            return Ok(new { fileName = model.File.FileName, filePath });
        }

        // Пример дополнительных эндпоинтов для демонстрации API (GET, PUT, DELETE)
        // Создадим сущность Item для демонстрации CRUD операций

        private static readonly List<Item> Items = new();

        // GET: api/file/items
        [HttpGet("items")]
        public IActionResult GetItems() => Ok(Items);

        // GET: api/file/items/{id}
        [HttpGet("items/{id}")]
        public IActionResult GetItem(int id)
        {
            var item = Items.Find(i => i.Id == id);
            return item == null ? NotFound() : Ok(item);
        }

        // POST: api/file/items
        [HttpPost("items")]
        public IActionResult CreateItem([FromBody] Item newItem)
        {
            newItem.Id = Items.Count + 1;
            Items.Add(newItem);
            return CreatedAtAction(nameof(GetItem), new { id = newItem.Id }, newItem);
        }

        // PUT: api/file/items/{id}
        [HttpPut("items/{id}")]
        public IActionResult UpdateItem(int id, [FromBody] Item updatedItem)
        {
            var index = Items.FindIndex(i => i.Id == id);
            if (index == -1) return NotFound();
            updatedItem.Id = id;
            Items[index] = updatedItem;
            return NoContent();
        }

        // DELETE: api/file/items/{id}
        [HttpDelete("items/{id}")]
        public IActionResult DeleteItem(int id)
        {
            var item = Items.Find(i => i.Id == id);
            if (item == null) return NotFound();
            Items.Remove(item);
            return NoContent();
        }
    }

    public class FileUploadModel
    {
        public Microsoft.AspNetCore.Http.IFormFile File { get; set; }
    }

    public class Item
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}
