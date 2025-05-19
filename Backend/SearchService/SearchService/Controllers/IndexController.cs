using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lucene.Net.Store;
using Lucene.Net.Index;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Util;
using Lucene.Net.Documents;
using Lucene.Net.Search;
using Lucene.Net.QueryParsers.Classic;

namespace SearchService.Controllers
{
    [ApiController]
    [Route("api/index")]
    public class IndexController : ControllerBase
    {
        private static readonly string IndexPath = "/app/lucene_index";
        private static readonly LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

        [HttpPost("index")]
        public IActionResult IndexFile([FromBody] IndexRequest request)
        {
            if (request == null || request.FileId == Guid.Empty)
                return BadRequest("Invalid index request.");

            using var dir = FSDirectory.Open(IndexPath);
            var analyzer = new StandardAnalyzer(AppLuceneVersion);

            var config = new IndexWriterConfig(AppLuceneVersion, analyzer);
            using var writer = new IndexWriter(dir, config);

            // Удаляем старые документы для этого FileId
            writer.DeleteDocuments(new Term("fileId", request.FileId.ToString()));

            var doc = new Document
            {
                new StringField("fileId", request.FileId.ToString(), Field.Store.YES),
                new Int32Field("userId", request.UserId, Field.Store.YES),
                new StringField("originalName", request.OriginalName ?? "", Field.Store.YES),
                new StringField("contentType", request.ContentType ?? "", Field.Store.YES),
                new TextField("content", request.TextContent ?? "", Field.Store.NO)
            };

            writer.AddDocument(doc);
            writer.Flush(triggerMerge: false, applyAllDeletes: true);

            return Ok();
        }

        [HttpDelete("delete/{fileId}")]
        public IActionResult DeleteFileIndex(Guid fileId)
        {
            if (fileId == Guid.Empty)
                return BadRequest("Invalid file id.");

            using var dir = FSDirectory.Open(IndexPath);
            var analyzer = new StandardAnalyzer(AppLuceneVersion);

            var config = new IndexWriterConfig(AppLuceneVersion, analyzer);
            using var writer = new IndexWriter(dir, config);

            writer.DeleteDocuments(new Term("fileId", fileId.ToString()));
            writer.Flush(triggerMerge: false, applyAllDeletes: true);

            return Ok();
        }

        [HttpGet("search")]
        public IActionResult Search([FromQuery] string term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return BadRequest("Empty search term.");

            using var dir = FSDirectory.Open(IndexPath);
            var analyzer = new StandardAnalyzer(AppLuceneVersion);
            using var reader = DirectoryReader.Open(dir);
            var searcher = new IndexSearcher(reader);

            var parser = new MultiFieldQueryParser(AppLuceneVersion, new[] { "content" }, analyzer)
            {
                DefaultOperator = Operator.OR
            };

            Query query;
            try
            {
                query = parser.Parse(term);
            }
            catch
            {
                return BadRequest("Invalid search query.");
            }

            TopDocs topDocs = searcher.Search(query, 50);
            var result = new List<Guid>();
            foreach (var scoreDoc in topDocs.ScoreDocs)
            {
                var doc = searcher.Doc(scoreDoc.Doc);
                if (Guid.TryParse(doc.Get("fileId"), out var id))
                {
                    result.Add(id);
                }
            }
            return Ok(result);
        }

        public class IndexRequest
        {
            public Guid FileId { get; set; }
            public int UserId { get; set; }
            public string OriginalName { get; set; }
            public string ContentType { get; set; }
            public string TextContent { get; set; }
        }
    }
}
