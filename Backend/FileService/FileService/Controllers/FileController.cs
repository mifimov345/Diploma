using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Threading.Tasks;

namespace FileService.Controllers
{
    /// <summary>
    /// Контроллер для загрузки файлов и демонстрации CRUD-операций с сущностью Item.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class FileController : ControllerBase
    {
        /// <summary>
        /// Загружает файл на сервер.
        /// </summary>
        /// <param name="model">Модель, содержащая загружаемый файл.</param>
        /// <returns>
        /// При успешной загрузке возвращает статус 200 OK с информацией о файле (имя и путь).
        /// Если файл не передан или пустой, возвращает 400 Bad Request.
        /// </returns>
        /// <remarks>
        /// Для загрузки файла используйте формат multipart/form-data.
        /// </remarks>
        [HttpPost("upload")]
        public async Task<IActionResult> UploadFile([FromForm] FileUploadModel model)
        {
            if (model.File == null || model.File.Length == 0)
                return BadRequest("No file uploaded.");

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

        // Статический список для хранения элементов.
        private static readonly List<Item> Items = new();

        /// <summary>
        /// Возвращает список всех элементов.
        /// </summary>
        /// <returns>Список элементов в формате JSON.</returns>
        [HttpGet("items")]
        public IActionResult GetItems() => Ok(Items);

        /// <summary>
        /// Возвращает элемент по его идентификатору.
        /// </summary>
        /// <param name="id">Идентификатор элемента.</param>
        /// <returns>Элемент, если найден; иначе 404 Not Found.</returns>
        [HttpGet("items/{id}")]
        public IActionResult GetItem(int id)
        {
            var item = Items.Find(i => i.Id == id);
            return item == null ? NotFound() : Ok(item);
        }

        /// <summary>
        /// Создаёт новый элемент.
        /// </summary>
        /// <param name="newItem">Новый элемент для создания.</param>
        /// <returns>
        /// При успешном создании возвращает статус 201 Created с созданным объектом.
        /// </returns>
        [HttpPost("items")]
        public IActionResult CreateItem([FromBody] Item newItem)
        {
            newItem.Id = Items.Count + 1;
            Items.Add(newItem);
            return CreatedAtAction(nameof(GetItem), new { id = newItem.Id }, newItem);
        }

        /// <summary>
        /// Обновляет существующий элемент.
        /// </summary>
        /// <param name="id">Идентификатор элемента, который нужно обновить.</param>
        /// <param name="updatedItem">Объект с обновлёнными данными.</param>
        /// <returns>
        /// При успешном обновлении возвращает статус 204 No Content; если элемент не найден – 404 Not Found.
        /// </returns>
        [HttpPut("items/{id}")]
        public IActionResult UpdateItem(int id, [FromBody] Item updatedItem)
        {
            var index = Items.FindIndex(i => i.Id == id);
            if (index == -1) return NotFound();
            updatedItem.Id = id;
            Items[index] = updatedItem;
            return NoContent();
        }

        /// <summary>
        /// Удаляет элемент по его идентификатору.
        /// </summary>
        /// <param name="id">Идентификатор элемента для удаления.</param>
        /// <returns>
        /// При успешном удалении возвращает статус 204 No Content; если элемент не найден – 404 Not Found.
        /// </returns>
        [HttpDelete("items/{id}")]
        public IActionResult DeleteItem(int id)
        {
            var item = Items.Find(i => i.Id == id);
            if (item == null) return NotFound();
            Items.Remove(item);
            return NoContent();
        }
    }

    /// <summary>
    /// Модель для загрузки файла.
    /// </summary>
    public class FileUploadModel
    {
        /// <summary>
        /// Файл, переданный через форму (тип IFormFile позволяет работать с multipart/form-data).
        /// </summary>
        public Microsoft.AspNetCore.Http.IFormFile File { get; set; }
    }

    /// <summary>
    /// Сущность для демонстрации CRUD-операций.
    /// </summary>
    public class Item
    {
        /// <summary>
        /// Идентификатор элемента.
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// Название элемента.
        /// </summary>
        public string Name { get; set; }
    }
}
