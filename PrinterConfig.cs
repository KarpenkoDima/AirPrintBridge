public class PrinterConfig
{
    // Имя, которое увидит пользователь на iPhone в списке принтеров
    public string DisplayName { get; set; } = "AirPrint Printer";

    // Имя принтера в Windows (из Device & Printers
    public string WindowsPrinterName { get; set; } = "";

    // Порт IPP сервера. 631 - стандартный, но требует прав администратора
    public int IppPort { get; set; } = 631;

    // Путь ресурса принтера в URI, iPhone будет слать POST на /printers/canon
    public string ResourcePath { get; set; } = "/printers/default";
}