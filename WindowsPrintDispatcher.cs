// WindowsPrintDispatcher.cs
using PdfiumViewer;
using System.Drawing.Printing;

namespace AirPrintBridge;

public class WindowsPrintDispatcher
{
    private readonly ILogger<WindowsPrintDispatcher> _logger;
    private readonly PrinterRuntime _printer;

    public WindowsPrintDispatcher(
        ILogger<WindowsPrintDispatcher> logger,
        PrinterRuntime printer)
    {
        _logger = logger;
        _printer = printer;
    }

    public async Task PrintAsync(byte[] documentData, string format, string jobName)
    {
        _logger.LogInformation(
            "Sending to Windows printer '{Printer}': job='{Job}', format={Format}",
            _printer.WindowsPrinterName, jobName, format);

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
                    throw new NotSupportedException(
                        "Apple/PWG raster input is not implemented yet. PDF input is supported.");

                default:
                    throw new NotSupportedException($"Document format '{format}' is not supported.");
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

        if (!availablePrinters.Contains(_printer.WindowsPrinterName, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "Printer '{Name}' not found. Available: {List}",
                _printer.WindowsPrinterName,
                string.Join(", ", availablePrinters));
            throw new InvalidOperationException(
                $"Printer '{_printer.WindowsPrinterName}' not found in system");
        }

        printDoc.PrinterSettings.PrinterName = _printer.WindowsPrinterName;
        printDoc.DocumentName = jobName;

        // StandardPrintController — без диалогового окна, тихая печать
        printDoc.PrintController = new StandardPrintController();

        _logger.LogInformation(
            "Starting print: '{Job}' → '{Printer}', {Pages} page(s)",
            jobName, _printer.WindowsPrinterName, pdfDoc.PageCount);

        printDoc.Print();

        _logger.LogInformation("Print job sent successfully");
    }
}
