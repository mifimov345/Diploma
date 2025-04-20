using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

// Используем псевдонимы для разрешения конфликтов и уточнения
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;
using PdfPigOptions = UglyToad.PdfPig.ParsingOptions;
using PdfPigPage = UglyToad.PdfPig.Content.Page;
using Excel = NPOI.SS.UserModel; // Общее пространство имен для Excel интерфейсов (ISheet, IRow, ICell, IWorkbook, DataFormatter)
using WordDOCX = NPOI.XWPF.UserModel; // Для DOCX (XWPFDocument)
using ExcelXLSX = NPOI.XSSF.UserModel; // Для XLSX (XSSFWorkbook)
using ExcelXLS = NPOI.HSSF.UserModel; // Для XLS (HSSFWorkbook)
// NPOI.HWPF и NPOI.HWPF.Extractor больше не нужны, т.к. убираем поддержку .doc

namespace SearchService.Services
{
    public interface ITextExtractorService
    {
        // Определяем, поддерживается ли формат для извлечения текста
        bool SupportsContentType(string? contentType, string? fileName);

        // Асинхронно извлекает текст. Возвращает null, если извлечь не удалось или формат не поддерживается.
        Task<string?> ExtractTextAsync(Stream fileStream, string? contentType, string? fileName);
    }

    public class TextExtractorService : ITextExtractorService
    {
        private readonly ILogger<TextExtractorService> _logger;

        public TextExtractorService(ILogger<TextExtractorService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // --- Определение поддерживаемых типов ---
        public bool SupportsContentType(string? contentType, string? fileName)
        {
            if (string.IsNullOrWhiteSpace(contentType) && string.IsNullOrWhiteSpace(fileName))
            {
                return false; // Не можем определить тип
            }

            string lowerContentType = contentType?.ToLowerInvariant() ?? string.Empty;
            string extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;

            // Проверяем поддерживаемые типы
            return
                // PDF
                lowerContentType == "application/pdf" || extension == ".pdf" ||
                // DOCX
                lowerContentType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document" || extension == ".docx" ||
                // XLSX
                lowerContentType == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" || extension == ".xlsx" ||
                // XLS (старый)
                lowerContentType == "application/vnd.ms-excel" || extension == ".xls" ||
                 // TXT / CSV / другие текстовые
                 lowerContentType.StartsWith("text/") || extension == ".txt" || extension == ".csv";
            // Поддержка .doc убрана из-за проблем совместимости NPOI.HWPF
            // lowerContentType == "application/msword" || extension == ".doc" ||
        }

        // --- Основной метод извлечения ---
        public async Task<string?> ExtractTextAsync(Stream fileStream, string? contentType, string? fileName)
        {
            // Используем ?? для безопасного логирования null имен
            string logFileName = fileName ?? "unknown_file";

            if (fileStream == null || !fileStream.CanRead)
            {
                _logger.LogWarning("ExtractTextAsync received null or unreadable stream for file '{FileName}'.", logFileName);
                return null;
            }

            // Если поток не поддерживает поиск (Seek), копируем его в MemoryStream,
            // так как NPOI и PdfPig могут этого требовать.
            MemoryStream? tempMemoryStream = null; // Nullable MemoryStream
            Stream streamToProcess = fileStream;    // По умолчанию используем исходный поток

            if (!fileStream.CanSeek)
            {
                _logger.LogWarning("Stream for file '{FileName}' is not seekable. Copying to MemoryStream.", logFileName);
                tempMemoryStream = new MemoryStream();
                try
                {
                    await fileStream.CopyToAsync(tempMemoryStream);
                    tempMemoryStream.Position = 0; // Перематываем MemoryStream в начало
                    streamToProcess = tempMemoryStream; // Дальше работаем с копией в памяти
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to copy non-seekable stream to MemoryStream for '{FileName}'.", logFileName);
                    tempMemoryStream?.Dispose(); // Закрываем MemoryStream при ошибке копирования
                    return null; // Не можем обработать
                }
            }
            else
            {
                // Если поток поддерживает Seek, просто сбрасываем его позицию
                try { streamToProcess.Position = 0; }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not reset position for seekable stream '{FileName}'.", logFileName); }
            }

            // Обрабатываем поток (либо исходный, либо копию в MemoryStream)
            // Оборачиваем использование временного потока в using, чтобы он гарантированно закрылся
            using (tempMemoryStream) // using(null) является no-op, это безопасно
            {
                string lowerContentType = contentType?.ToLowerInvariant() ?? string.Empty;
                string extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;
                _logger.LogInformation("Attempting text extraction for '{FileName}' (Type: '{ContentType}', Ext: '{Extension}')", logFileName, contentType ?? "N/A", extension);

                try
                {
                    // Запускаем синхронные операции извлечения в фоновом потоке
                    return await Task.Run<string?>(() => // Явно указываем тип возвращаемого значения Task<string?>
                    {
                        // Определяем тип и вызываем соответствующий метод
                        if (lowerContentType == "application/pdf" || extension == ".pdf")
                        {
                            return ExtractPdfText(streamToProcess, logFileName);
                        }
                        else if (lowerContentType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document" || extension == ".docx")
                        {
                            return ExtractDocxText(streamToProcess, logFileName);
                        }
                        else if (lowerContentType == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" || extension == ".xlsx")
                        {
                            return ExtractXlsxText(streamToProcess, logFileName);
                        }
                        // Поддержка .doc убрана
                        // else if (lowerContentType == "application/msword" || extension == ".doc")
                        // {
                        //     return ExtractDocText(streamToProcess, logFileName);
                        // }
                        else if (lowerContentType == "application/vnd.ms-excel" || extension == ".xls")
                        {
                            return ExtractXlsText(streamToProcess, logFileName);
                        }
                        else if (lowerContentType.StartsWith("text/") || extension == ".txt" || extension == ".csv")
                        {
                            return ExtractPlainText(streamToProcess, logFileName);
                        }
                        else
                        {
                            _logger.LogWarning("Unsupported file type for text extraction: '{FileName}' (ContentType: '{ContentType}', Extension: '{Extension}')", logFileName, contentType ?? "N/A", extension);
                            return null; // Возвращаем null для неподдерживаемых типов
                        }
                    });
                }
                catch (Exception ex) // Ловим ошибки из Task.Run или самой лямбды
                {
                    _logger.LogError(ex, "Error during text extraction wrapper Task.Run for file '{FileName}'.", logFileName);
                    return null;
                }
            } // tempMemoryStream (если был создан) будет здесь закрыт
            // Исходный fileStream не закрывается, так как он пришел извне
        }

        // --- Вспомогательные методы для каждого типа ---

        private string? ExtractPdfText(Stream stream, string fileName)
        {
            _logger.LogDebug("Using PdfPig extractor for '{FileName}'", fileName);
            try
            {
                // PdfPigDoc.Open требует возможности Seek, мы это обеспечили выше
                // Используем using, т.к. PdfDocument реализует IDisposable
                using (var document = PdfPigDoc.Open(stream, new PdfPigOptions())) // Можно передать опции парсинга
                {
                    if (document.NumberOfPages == 0)
                    {
                        _logger.LogWarning("PdfPig: Document '{FileName}' has 0 pages.", fileName);
                        return string.Empty; // Или null? Пустая строка логичнее для пустого документа.
                    }

                    var textBuilder = new StringBuilder();
                    // Итерируем по страницам (нумерация с 1)
                    for (var i = 1; i <= document.NumberOfPages; i++)
                    {
                        try
                        {
                            PdfPigPage page = document.GetPage(i);
                            textBuilder.Append(page.Text);
                            // Добавляем разделитель, чтобы текст с разных страниц не сливался
                            if (i < document.NumberOfPages) textBuilder.Append("\n\n--- Page Break ---\n\n");
                        }
                        catch (Exception pageEx)
                        {
                            _logger.LogError(pageEx, "PdfPig: Error processing page {PageNumber} of '{FileName}'. Skipping page.", i, fileName);
                        }
                    }
                    _logger.LogInformation("PdfPig extracted text from {PageCount} pages in '{FileName}'. Total length: {Length}", document.NumberOfPages, fileName, textBuilder.Length);
                    return textBuilder.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PdfPig failed to process '{FileName}'", fileName);
                return null;
            }
        }

        private string? ExtractDocxText(Stream stream, string fileName)
        {
            _logger.LogDebug("Using NPOI XWPF extractor for '{FileName}'", fileName);
            try
            {
                // XWPFDocument не IDisposable, using не нужен
                var document = new WordDOCX.XWPFDocument(stream);
                var textBuilder = new StringBuilder();
                foreach (var para in document.Paragraphs)
                {
                    if (para != null && !string.IsNullOrEmpty(para.Text)) // Проверка на null
                    {
                        textBuilder.AppendLine(para.Text);
                    }
                }
                // Дополнительно: извлечение из таблиц (упрощенно)
                foreach (var table in document.Tables)
                {
                    if (table == null) continue;
                    foreach (var row in table.Rows)
                    {
                        if (row == null) continue;
                        foreach (var cell in row.GetTableCells())
                        {
                            if (cell != null && !string.IsNullOrEmpty(cell.GetText()))
                            {
                                textBuilder.Append(cell.GetText()?.Trim()).Append("\t"); // Табуляция между ячейками
                            }
                        }
                        textBuilder.AppendLine(); // Новая строка после строки таблицы
                    }
                }

                _logger.LogInformation("NPOI XWPF extracted text from '{FileName}'. Length: {Length}", fileName, textBuilder.Length);
                // document.Close(); // Можно вызвать, но не обязательно
                return textBuilder.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NPOI XWPF failed to extract text from '{FileName}'", fileName);
                return null;
            }
        }

        private string? ExtractXlsxText(Stream stream, string fileName)
        {
            _logger.LogDebug("Using NPOI XSSF extractor for '{FileName}'", fileName);
            return ExtractExcelText(stream, fileName, true); // true = XLSX
        }

        private string? ExtractXlsText(Stream stream, string fileName)
        {
            _logger.LogDebug("Using NPOI HSSF extractor for '{FileName}'", fileName);
            return ExtractExcelText(stream, fileName, false); // false = XLS
        }

        // Общий метод для XLS и XLSX
        private string? ExtractExcelText(Stream stream, string fileName, bool isXlsx)
        {
            string format = isXlsx ? "XSSF" : "HSSF";
            try
            {
                Excel.IWorkbook workbook; // Используем псевдоним Excel
                if (isXlsx) { workbook = new ExcelXLSX.XSSFWorkbook(stream); }
                else { workbook = new ExcelXLS.HSSFWorkbook(stream); }

                // IWorkbook не IDisposable, using не нужен
                var textBuilder = new StringBuilder();
                // DataFormatter помогает получить строковое представление разных типов ячеек
                var formatter = new Excel.DataFormatter();

                _logger.LogDebug("Processing {SheetCount} sheets in '{FileName}'", workbook.NumberOfSheets, fileName);
                for (int i = 0; i < workbook.NumberOfSheets; i++)
                {
                    Excel.ISheet? sheet = workbook.GetSheetAt(i); // ISheet может быть null
                    if (sheet != null)
                    {
                        _logger.LogDebug("Processing Sheet '{SheetName}' ({RowCount} rows)", sheet.SheetName, sheet.PhysicalNumberOfRows);
                        foreach (Excel.IRow? row in sheet) // IRow может быть null
                        {
                            if (row == null) continue;
                            foreach (Excel.ICell? cell in row) // ICell может быть null
                            {
                                if (cell != null)
                                {
                                    // Используем DataFormatter для получения строки
                                    // Второй аргумент null заставляет использовать формат ячейки по умолчанию
                                    string cellText = formatter.FormatCellValue(cell, null)?.Trim() ?? "";
                                    if (!string.IsNullOrEmpty(cellText))
                                    {
                                        textBuilder.Append(cellText).Append("\t"); // Табуляция как разделитель ячеек
                                    }
                                }
                            }
                            textBuilder.AppendLine(); // Новая строка Excel -> новая строка текста
                        }
                    }
                }
                _logger.LogInformation("NPOI {Format} extracted text from '{FileName}'. Length: {Length}", format, fileName, textBuilder.Length);
                // workbook.Close(); // Можно вызвать Close
                return textBuilder.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NPOI {Format} failed for '{FileName}'", format, fileName);
                return null;
            }
        }

        // Метод для .doc удален из-за проблем совместимости NPOI.HWPF
        // private string? ExtractDocText(Stream stream, string fileName) { ... }

        private string? ExtractPlainText(Stream stream, string fileName)
        {
            _logger.LogDebug("Using StreamReader for '{FileName}'", fileName);
            try
            {
                // StreamReader нужно использовать с using, т.к. он IDisposable
                // leaveOpen: true - чтобы не закрывать базовый stream
                using (var reader = new StreamReader(stream, Encoding.UTF8, true, bufferSize: 1024, leaveOpen: true))
                {
                    string text = reader.ReadToEnd();
                    _logger.LogInformation("StreamReader extracted text from '{FileName}'. Length: {Length}", fileName, text.Length);
                    return text;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StreamReader failed for '{FileName}'", fileName);
                return null;
            }
        }
    }
}