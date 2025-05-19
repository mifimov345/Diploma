using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using PdfPigDoc = UglyToad.PdfPig.PdfDocument;
using PdfPigOptions = UglyToad.PdfPig.ParsingOptions;
using PdfPigPage = UglyToad.PdfPig.Content.Page;
using Excel = NPOI.SS.UserModel;
using WordDOCX = NPOI.XWPF.UserModel;
using ExcelXLSX = NPOI.XSSF.UserModel;
using ExcelXLS = NPOI.HSSF.UserModel;

namespace SearchService.Services
{

    public class TextExtractorService : ITextExtractorService
    {
        private readonly ILogger<TextExtractorService> _logger;

        public TextExtractorService(ILogger<TextExtractorService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool SupportsContentType(string? contentType, string? fileName)
        {
            if (string.IsNullOrWhiteSpace(contentType) && string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            string lowerContentType = contentType?.ToLowerInvariant() ?? string.Empty;
            string extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;

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
        }

        public async Task<string?> ExtractTextAsync(Stream fileStream, string? contentType, string? fileName)
        {
            string logFileName = fileName ?? "unknown_file";

            if (fileStream == null || !fileStream.CanRead)
            {
                //_logger.LogWarning("ExtractTextAsync received null or unreadable stream for file '{FileName}'.", logFileName);
                return null;
            }

            MemoryStream? tempMemoryStream = null;
            Stream streamToProcess = fileStream;

            if (!fileStream.CanSeek)
            {
                //_logger.LogWarning("Stream for file '{FileName}' is not seekable. Copying to MemoryStream.", logFileName);
                tempMemoryStream = new MemoryStream();
                try
                {
                    await fileStream.CopyToAsync(tempMemoryStream);
                    tempMemoryStream.Position = 0;
                    streamToProcess = tempMemoryStream;
                }
                catch (Exception ex)
                {
                    //_logger.LogError(ex, "Failed to copy non-seekable stream to MemoryStream for '{FileName}'.", logFileName);
                    tempMemoryStream?.Dispose();
                    return null;
                }
            }
            else
            {
                try { streamToProcess.Position = 0; }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not reset position for seekable stream '{FileName}'.", logFileName); }
            }

            using (tempMemoryStream)
            {
                string lowerContentType = contentType?.ToLowerInvariant() ?? string.Empty;
                string extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;
                //_logger.LogInformation("Attempting text extraction for '{FileName}' (Type: '{ContentType}', Ext: '{Extension}')", logFileName, contentType ?? "N/A", extension);

                try
                {
                    return await Task.Run<string?>(() =>
                    {
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
                            //_logger.LogWarning("Unsupported file type for text extraction: '{FileName}' (ContentType: '{ContentType}', Extension: '{Extension}')", logFileName, contentType ?? "N/A", extension);
                            return null;
                        }
                    });
                }
                catch (Exception ex)
                {
                    //_logger.LogError(ex, "Error during text extraction wrapper Task.Run for file '{FileName}'.", logFileName);
                    return null;
                }
            }
        }


        private string? ExtractPdfText(Stream stream, string fileName)
        {
            //_logger.LogDebug("Using PdfPig extractor for '{FileName}'", fileName);
            try
            {
                using (var document = PdfPigDoc.Open(stream, new PdfPigOptions()))
                {
                    if (document.NumberOfPages == 0)
                    {
                        //_logger.LogWarning("PdfPig: Document '{FileName}' has 0 pages.", fileName);
                        return string.Empty;
                    }

                    var textBuilder = new StringBuilder();
                    for (var i = 1; i <= document.NumberOfPages; i++)
                    {
                        try
                        {
                            PdfPigPage page = document.GetPage(i);
                            textBuilder.Append(page.Text);
                            if (i < document.NumberOfPages) textBuilder.Append("\n\n--- Page Break ---\n\n");
                        }
                        catch (Exception pageEx)
                        {
                            //_logger.LogError(pageEx, "PdfPig: Error processing page {PageNumber} of '{FileName}'. Skipping page.", i, fileName);
                        }
                    }
                    //_logger.LogInformation("PdfPig extracted text from {PageCount} pages in '{FileName}'. Total length: {Length}", document.NumberOfPages, fileName, textBuilder.Length);
                    return textBuilder.ToString();
                }
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "PdfPig failed to process '{FileName}'", fileName);
                return null;
            }
        }

        private string? ExtractDocxText(Stream stream, string fileName)
        {
            //_logger.LogDebug("Using NPOI XWPF extractor for '{FileName}'", fileName);
            try
            {
                var document = new WordDOCX.XWPFDocument(stream);
                var textBuilder = new StringBuilder();
                foreach (var para in document.Paragraphs)
                {
                    if (para != null && !string.IsNullOrEmpty(para.Text))
                    {
                        textBuilder.AppendLine(para.Text);
                    }
                }
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
                                textBuilder.Append(cell.GetText()?.Trim()).Append("\t");
                            }
                        }
                        textBuilder.AppendLine();
                    }
                }

                //_logger.LogInformation("NPOI XWPF extracted text from '{FileName}'. Length: {Length}", fileName, textBuilder.Length);
                return textBuilder.ToString();
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "NPOI XWPF failed to extract text from '{FileName}'", fileName);
                return null;
            }
        }

        private string? ExtractXlsxText(Stream stream, string fileName)
        {
            //_logger.LogDebug("Using NPOI XSSF extractor for '{FileName}'", fileName);
            return ExtractExcelText(stream, fileName, true); // true = XLSX
        }

        private string? ExtractXlsText(Stream stream, string fileName)
        {
            //_logger.LogDebug("Using NPOI HSSF extractor for '{FileName}'", fileName);
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

                var textBuilder = new StringBuilder();
                var formatter = new Excel.DataFormatter();

                //_logger.LogDebug("Processing {SheetCount} sheets in '{FileName}'", workbook.NumberOfSheets, fileName);
                for (int i = 0; i < workbook.NumberOfSheets; i++)
                {
                    Excel.ISheet? sheet = workbook.GetSheetAt(i);
                    if (sheet != null)
                    {
                        //_logger.LogDebug("Processing Sheet '{SheetName}' ({RowCount} rows)", sheet.SheetName, sheet.PhysicalNumberOfRows);
                        foreach (Excel.IRow? row in sheet)
                        {
                            if (row == null) continue;
                            foreach (Excel.ICell? cell in row)
                            {
                                if (cell != null)
                                {
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
                //_logger.LogInformation("NPOI {Format} extracted text from '{FileName}'. Length: {Length}", format, fileName, textBuilder.Length);
                return textBuilder.ToString();
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "NPOI {Format} failed for '{FileName}'", format, fileName);
                return null;
            }
        }

        private string? ExtractPlainText(Stream stream, string fileName)
        {
            //_logger.LogDebug("Using StreamReader for '{FileName}'", fileName);
            try
            {
                using (var reader = new StreamReader(stream, Encoding.UTF8, true, bufferSize: 1024, leaveOpen: true))
                {
                    string text = reader.ReadToEnd();
                    //_logger.LogInformation("StreamReader extracted text from '{FileName}'. Length: {Length}", fileName, text.Length);
                    return text;
                }
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "StreamReader failed for '{FileName}'", fileName);
                return null;
            }
        }
    }
}