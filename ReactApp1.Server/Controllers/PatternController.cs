using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileSystemGlobbing.Internal;
using PatternTracker.Server.Interfaces;

namespace PatternTracker.Server.Controllers;

[ApiController]
[Route("[controller]")]
public class PatternController : ControllerBase
{
    private readonly IPatternService _patternService;

    public PatternController(IPatternService patternService)
    {
        _patternService = patternService;
    }

    [HttpGet("/patterns")]
    public ActionResult<IReadOnlyCollection<Pattern>> GetPatterns([FromHeader(Name = "Cookie")] string cookie)
    {
        var psuid = Details.GetPsuidCookie(cookie);
        return psuid != null
            ? Ok(_patternService.GetPatterns(psuid))
            : NotFound();
    }

    [HttpPost("/pattern")]
    public async Task<IActionResult> AddPattern([FromHeader(Name = "Cookie")] string cookie, HttpContext context)
    {
        var psuid = Details.GetPsuidCookie(cookie);
        if (psuid == null)
            return Unauthorized();

        var pdfPath = await PdfHelper.SaveToFile(context.Request.Body);
        if (pdfPath == null)
            return Unauthorized();

        var res = await Details.ParsePdfAndAddToDb(pdfPath, psuid, trackerDb);
        File.Delete(pdfPath);

        return res != null
        ? Results.Ok(res)
        : Results.Problem();
    }

    [HttpGet("/pattern")]
    public IActionResult<Task<Pattern>> GetPattern([FromHeader(Name = "Cookie")] string cookie, int patternId)
    {
        var psuid = Details.GetPsuidCookie(cookie);
        if (psuid == null)
            return Unauthorized();

        var pattern = Details.GetPattern(psuid, patternId, trackerDb);

        return pattern != null
            ? Ok(pattern)
            : Problem();
    }

    [HttpDelete("/pattern")]
    public IActionResult DeletePattern([FromHeader(Name = "Cookie")] string cookie, int patternId)
    {
        var psuid = Details.GetPsuidCookie(cookie);
        if (psuid == null)
            return Unauthorized();

        try
        {
            _patternService.DeletePattern(psuid, patternId);
        }
        var psuid = Details.GetPsuidCookie(cookie);
        if (psuid == null)
            return Unauthorized();

        var pattern = Details.GetPattern(psuid, id, trackerDb);

        return pattern != null
            ? Ok(pattern)
            : Problem();
    }

    [HttpPost]
    public Pattern Post()
    {
        string dataDir = "C:\\Users\\Polina\\Desktop\\parsed files\\CrossStichViewer\\1\\";
        return PdfParser.Parse(dataDir + "multiple_lists.pdf", 73, 130);
    }

    [HttpGet]
    public string Get()
    {
        
        //string dataDir = @"C:\Users\Polina\Desktop\PS\my projects\CrossStich\files\";
        ////string fileName = "scheme_70_50_new";
        //string fileName = "scheme_70_50_old";
        //string pdfFilePath = dataDir + fileName + ".pdf";

        ////Document pdfDocument = new Document(pdfFilePath);
        ////pdfDocument.Save(dataDir + "output_out.html", SaveFormat.Html);

        //var mupdf = new Mupdf(pdfFilePath);
        //var fontsLink = mupdf.SaveFonts();
        //fontsLink = "/static_fonts/123/";

        ////var fonts = Mupdf.GetFontsInfo(pdfFilePath);
        //var pattern = PdfParser.ParsePdf(dataDir + fileName + ".pdf", 70, 50);
        //pattern.WriteToFile(dataDir + fileName + ".json");

        //// тут типа отправляем паттерн, получаем список с описанием ниток
        //// пока захардкожу его
        //List<Color.FlossDescription> flosses = new List<Color.FlossDescription>
        //{
        //    new Color.FlossDescription("Anchor", new Dictionary<string, string>{ })
        //};

        //JObject obj = JObject.FromObject(new
        //{
        //    fontsLink = fontsLink,
        //    pattern = pattern
        //});

        //return JsonConvert.SerializeObject(obj, Formatting.None);
        return null;
    }
}