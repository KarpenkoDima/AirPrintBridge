// WindowsPrintDispatcher.cs
using Microsoft.Extensions.Options;
using PdfiumViewer;
using System.Drawing.Printing;

namespace AirPrintBridge;

public class WindowsPrintDispatcher
{
    private readonly ILogger<WindowsPrintDispatcher> _logger;
    private readonly PrinterConfig _config;

    public WindowsPrintDispatcher(
        ILogger<WindowsPrintDispatcher> logger,
        IOptions<PrinterConfig> config)
    {
        _logger = logger;
        _config = config.Value;
    }

    public async Task PrintAsync(byte[] documentData, string format, string jobName)
    {
        _logger.LogInformation(
            "Sending to Windows printer '{Printer}': job='{Job}', format={Format}",
            _config.WindowsPrinterName, jobName, format);

        // Запускаем печать в отдельном потоке — GDI/COM не любит async
        await Task.Run(() =>
        {
            switch (format.ToLowerInvariant())
            {
                case "application/pdf":
                    PrintPdf(documentData, jobName);
                    break;

                case "image/urf":
                case "image/pwg-raster":
                    // PWG Raster — растровый формат Apple, конвертируем через temp PDF
                    // На этапе MVP: сохраняем файл и логируем, полная конвертация — следующий шаг
                    _logger.LogWarning(
                        "PWG/URF format received. Full raster support coming soon. " +
                        "Saved to temp for inspection.");
                    var path = Path.Combine(Path.GetTempPath(),
                        $"airprint_raster_{DateTime.Now:yyyyMMdd_HHmmss}.bin");
                    File.WriteAllBytes(path, documentData);
                    break;

                default:
                    _logger.LogWarning("Unknown document format: {Format}", format);
                    break;
            }
        });
    }

    private void PrintPdf(byte[] pdfData, string jobName)
    {
        // PdfiumViewer использует нативную библиотеку pdfium для рендеринга PDF
        // LoadPdf принимает поток, что удобнее временных файлов
        using var stream = new MemoryStream(pdfData);
        using var pdfDoc = PdfDocument.Load(stream);
        using var printDoc = pdfDoc.CreatePrintDocument();

        // Проверяем что принтер существует в системе
        var availablePrinters = PrinterSettings.InstalledPrinters
            .Cast<string>().ToList();

        if (!availablePrinters.Contains(_config.WindowsPrinterName))
        {
            _logger.LogError(
                "Printer '{Name}' not found. Available: {List}",
                _config.WindowsPrinterName,
                string.Join(", ", availablePrinters));
            throw new InvalidOperationException(
                $"Printer '{_config.WindowsPrinterName}' not found in system");
        }

        printDoc.PrinterSettings.PrinterName = _config.WindowsPrinterName;
        printDoc.DocumentName = jobName;

        // StandardPrintController — без диалогового окна, тихая печать
        printDoc.PrintController = new StandardPrintController();

        _logger.LogInformation(
            "Starting print: '{Job}' → '{Printer}', {Pages} page(s)",
            jobName, _config.WindowsPrinterName, pdfDoc.PageCount);

        printDoc.Print();

        _logger.LogInformation("Print job sent successfully");
    }
}