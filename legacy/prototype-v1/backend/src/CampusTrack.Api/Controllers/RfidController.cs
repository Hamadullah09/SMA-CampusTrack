using CampusTrack.Api.Data;
using CampusTrack.Api.Domain;
using CampusTrack.Api.Dtos;
using CampusTrack.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Controllers;

/// <summary>
/// Ingestion endpoint for the UHF fixed readers (or the small middleware
/// that talks to them over LLRP / vendor SDK). Each antenna hit is POSTed
/// here; the sequence engine works out entry vs exit from antenna order.
/// Secured by a shared API key header instead of JWT because readers are
/// devices, not users.
/// </summary>
[ApiController]
[Route("api/rfid")]
public class RfidController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly RfidSequenceEngine _engine;
    private readonly IConfiguration _cfg;

    public RfidController(AppDbContext db, RfidSequenceEngine engine, IConfiguration cfg)
    {
        _db = db; _engine = engine; _cfg = cfg;
    }

    [HttpPost("reads")]
    public async Task<IActionResult> IngestReads(RfidBatchRequest req,
        [FromHeader(Name = "X-Reader-ApiKey")] string? apiKey)
    {
        var expected = _cfg["Rfid:ApiKey"];
        if (!string.IsNullOrEmpty(expected) && apiKey != expected)
            return Unauthorized();

        var codes = req.Reads.Select(r => r.ReaderCode).Distinct().ToList();
        var readers = await _db.RfidReaders
            .Where(r => codes.Contains(r.ReaderCode) && r.IsActive)
            .ToDictionaryAsync(r => r.ReaderCode);

        int accepted = 0;
        foreach (var read in req.Reads)
        {
            if (!readers.TryGetValue(read.ReaderCode, out var reader)) continue;
            if (read.AntennaNo < 1 || read.AntennaNo > reader.AntennaCount) continue;

            var time = read.ReadTime?.ToUniversalTime() ?? DateTime.UtcNow;

            _db.RawRfidReads.Add(new RawRfidRead
            {
                ReaderId = reader.Id, AntennaNo = read.AntennaNo,
                Epc = read.Epc, ReadTime = time
            });
            _engine.AddRead(reader.Id, read.Epc, read.AntennaNo, time);
            accepted++;
        }
        await _db.SaveChangesAsync();
        return Ok(new { accepted, received = req.Reads.Count });
    }
}
