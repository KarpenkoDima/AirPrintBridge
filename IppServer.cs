// IppServer.cs
using Makaretu.Dns;
using Microsoft.Extensions.Options;
using System.IO.Pipelines;
using System.Net;

namespace AirPrintBridge;

public class IppServer : BackgroundService
{
    private readonly ILogger<IppServer> _logger;
    private readonly PrinterConfig _config;
    private HttpListener? _listener;
    private Task? _listenerTask;
    private CancellationTokenSource? _cts;

    // IppServer.cs — добавить в конструктор зависимость
    private readonly WindowsPrintDispatcher _printDispatcher;

    // Константы операций IPP (из RFC 8011)
    private const short VersionIpp11 = 0x0101;
    private const short StatusOk = 0x0000;
    private const short StatusServerError = 0x0500;
    private const short OpGetPrinterAttribs = 0x000B;
    private const short OpPrintJob = 0x0002;
    private const short OpValidateJob = 0x0004;
    private const short OpGetJobs = 0x000A;
    private const short OpCancelJob = 0x0008;

    // IPP attribute group tags
    private const byte TagOperationAttribs = 0x01;
    private const byte TagPrinterAttribs = 0x04;
    private const byte TagEndOfAttribs = 0x03;

    // IPP value tags (типы значений атрибутов)
    private const byte ValueTagInteger = 0x21;
    private const byte ValueTagBoolean = 0x22;
    private const byte ValueTagEnum = 0x23;
    private const byte ValueTagOctetString = 0x30;
    private const byte ValueTagDateTime = 0x31;
    private const byte ValueTagKeyword = 0x44;
    private const byte ValueTagUri = 0x45;
    private const byte ValueTagCharset = 0x47;
    private const byte ValueTagNatLang = 0x48;
    private const byte ValueTagMimeType = 0x49;
    private const byte ValueTagNameNoLang = 0x42;
    private const byte ValueTagTextNoLang = 0x41;
    private const byte ValueTagRangeOfInt = 0x33;
    private const byte ValueTagResolution = 0x32;

    public IppServer(ILogger<IppServer> logger, IOptions<PrinterConfig> config, WindowsPrintDispatcher printDispatcher)
    {
        _logger = logger;
        _config = config.Value;
        _printDispatcher = printDispatcher;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {/*
        // КРИТИЧЕСКОЕ ИСПРАВЛЕНИЕ:
        // HttpListener с "+" требует netsh регистрации или прав администратора
        // Вместо этого слушаем на конкретных IP адресах

        var localAddresses = System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName())
            .Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .ToList();

        _listener = new HttpListener();

        // Добавляем localhost
        _listener.Prefixes.Add($"http://localhost:{_config.IppPort}/");
        _listener.Prefixes.Add($"http://127.0.0.1:{_config.IppPort}/");

        // Добавляем ВСЕ локальные IP адреса (включая 192.168.0.107)
        foreach (var addr in localAddresses)
        {
            var prefix = $"http://{addr}:{_config.IppPort}/";
            _logger.LogInformation("Adding HTTP prefix: {Prefix}", prefix);
            _listener.Prefixes.Add(prefix);
        }

        try
        {
            _listener.Start();
            _logger.LogInformation("IPP server started successfully on port {Port}", _config.IppPort);
            _logger.LogInformation("Listening on {Count} addresses", _listener.Prefixes.Count);
        }
        catch (HttpListenerException ex)
        {
            _logger.LogError(ex,
                "Failed to start HTTP listener. Error code: {Code}. " +
                "Try running as Administrator or use netsh to register URLs.",
                ex.ErrorCode);
            throw;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listenerTask = Task.Run(() => ListenLoop(_cts.Token), _cts.Token);

        return Task.CompletedTask;*/
        _listener = new HttpListener();

        // УДАЛИТЕ цикл с foreach и GetHostAddresses
        // Оставьте ТОЛЬКО ЭТУ ОДНУ СТРОКУ со знаком "+". 
        // Знак "+" означает "принимать запросы для любых IP и любых Host-заголовков"
        /* _listener.Prefixes.Add($"http://+:{_config.IppPort}/");

         try
         {
             _listener.Start();
             _logger.LogInformation("IPP server started successfully on port {Port}", _config.IppPort);
         }
         catch (HttpListenerException ex)
         {
             _logger.LogError(ex, "Failed to start HTTP listener. RUN THE APP AS ADMINISTRATOR!");
             throw;
         }

         _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
         _listenerTask = Task.Run(() => ListenLoop(_cts.Token), _cts.Token);

         return Task.CompletedTask;*/

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_config.IppPort}/");

        // Биндим на ВСЕ non-loopback IPv4 адреса, чтобы не зависеть от порядка,
        // в котором Dns.GetHostAddresses() возвращает интерфейсы.
        // На Windows+WSL первым может оказаться 172.19.x.x (WSL), а не 192.168.x.x (Ethernet).
        // mDNS рекламирует 192.168.x.x, поэтому HttpListener ОБЯЗАН слушать на нём тоже.
        var allLocalIps = System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName())
            .Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                         && !System.Net.IPAddress.IsLoopback(ip))
            .ToList();

        foreach (var ip in allLocalIps)
        {
            _listener.Prefixes.Add($"http://{ip}:{_config.IppPort}/");
            _logger.LogInformation("Binding IppServer to IP: {IP}:{Port}", ip, _config.IppPort);
        }

        try
        {
            _listener.Start();
            _logger.LogInformation("IPP server started successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start HttpListener. Port might be busy.");
            throw;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listenerTask = Task.Run(() => ListenLoop(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await _listener!.GetContextAsync();
                // Каждый запрос обрабатываем в отдельной задаче, не блокируем основной цикл
                _ = Task.Run(() => HandleRequest(context), ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break; // нормальная остановка
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in listener loop");
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext context)
    {
        var req = context.Request;
        var resp = context.Response;

        _logger.LogInformation("← {Method} {Url} | Content-Type: {Ct}",
            req.HttpMethod, req.Url?.PathAndQuery, req.ContentType);

        try
        {
            // IPP всегда приходит как POST с Content-Type: application/ipp
            if (req.HttpMethod == "POST" && req.ContentType?.Contains("application/ipp") == true)
            {
                await HandleIppRequest(req, resp);
            }
            else
            {
                // Браузерный запрос — возвращаем простую страницу для отладки
                await HandleHttpRequest(req, resp);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling request");
            resp.StatusCode = 500;
        }
        finally
        {
            resp.Close();
        }
    }

    private async Task HandleIppRequest(HttpListenerRequest req, HttpListenerResponse resp)
    {
        // Читаем весь IPP пакет в память
        using var ms = new MemoryStream();
        await req.InputStream.CopyToAsync(ms);
        var data = ms.ToArray();

        if (data.Length < 8)
        {
            _logger.LogWarning("IPP packet too short: {Len} bytes", data.Length);
            resp.StatusCode = 400;
            return;
        }

        // Первые 8 байт заголовка IPP:
        // [0-1] version, [2-3] operation-id, [4-7] request-id
        var version = (short)((data[0] << 8) | data[1]);
        var operation = (short)((data[2] << 8) | data[3]);
        var requestId = (int)((data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7]);

        _logger.LogInformation(
            "IPP operation: 0x{Op:X4} ({OpName}), request-id: {Id}",
            operation, GetOperationName(operation), requestId);

        byte[] ippResponse = operation switch
        {
            OpGetPrinterAttribs => BuildGetPrinterAttributesResponse(requestId),
            OpValidateJob => BuildSimpleSuccessResponse(requestId),
            OpPrintJob => await HandlePrintJob(req, requestId, data),            
            OpGetJobs => BuildGetJobsResponse(requestId),
            OpCancelJob => BuildSimpleSuccessResponse(requestId),
            _ => BuildSimpleSuccessResponse(requestId)
        };

        resp.ContentType = "application/ipp";
        resp.StatusCode = 200; // HTTP статус всегда 200! Статус IPP — внутри пакета.
        resp.ContentLength64 = ippResponse.Length;
        await resp.OutputStream.WriteAsync(ippResponse);

        _logger.LogInformation("→ IPP response: {Len} bytes", ippResponse.Length);
    }

    private byte[] BuildGetPrinterAttributesResponse(int requestId)
    {
        var w = new IppWriter();

        // --- Заголовок IPP ---
        w.WriteVersion(VersionIpp11);
        w.WriteShort(StatusOk);
        w.WriteInt(requestId);

        // --- Operation Attributes ---
        w.WriteByte(TagOperationAttribs);
        w.WriteAttribute(ValueTagCharset, "attributes-charset", "utf-8");
        w.WriteAttribute(ValueTagNatLang, "attributes-natural-language", "en");

        // --- Printer Attributes ---
        w.WriteByte(TagPrinterAttribs);

        var hostName = "AirPrint-Bridge-Server.local";
        var printerUri = $"ipp://{hostName}:{_config.IppPort}/{_config.ResourcePath}";

        w.WriteAttribute(ValueTagUri, "printer-uri-supported", printerUri);

        // 1. КРИТИЧНО: UUID должен быть с префиксом urn:uuid: и точно совпадать с mDNS
        w.WriteAttribute(ValueTagUri, "printer-uuid", "urn:uuid:5365e660-f657-41a6-88a4-0994132ad372");

        w.WriteAttribute(ValueTagKeyword, "uri-security-supported", "none");
        w.WriteAttribute(ValueTagKeyword, "uri-authentication-supported", "none");
        w.WriteAttribute(ValueTagNameNoLang, "printer-name", _config.DisplayName);
        w.WriteAttribute(ValueTagTextNoLang, "printer-make-and-model", "Canon MF3010");

        w.WriteIntAttribute("printer-state", ValueTagEnum, 3);
        w.WriteAttribute(ValueTagKeyword, "printer-state-reasons", "none");
        w.WriteAttribute(ValueTagKeyword, "ipp-versions-supported", "1.1");
        w.WriteAttributeAdditional(ValueTagKeyword, "2.0");

        w.WriteIntAttribute("operations-supported", ValueTagEnum, OpPrintJob);
        w.WriteIntAttributeAdditional(ValueTagEnum, OpValidateJob);
        w.WriteIntAttributeAdditional(ValueTagEnum, OpGetPrinterAttribs);
        w.WriteIntAttributeAdditional(ValueTagEnum, OpGetJobs);
        w.WriteIntAttributeAdditional(ValueTagEnum, OpCancelJob);

        w.WriteAttribute(ValueTagCharset, "charset-configured", "utf-8");
        w.WriteAttribute(ValueTagCharset, "charset-supported", "utf-8");
        w.WriteAttribute(ValueTagNatLang, "natural-language-configured", "en");
        w.WriteAttribute(ValueTagNatLang, "generated-natural-language-supported", "en");

        // 2. КРИТИЧНО: Размеры бумаги. Без этого ломается UI на iPhone!
        w.WriteAttribute(ValueTagKeyword, "media-default", "iso_a4_210x297mm");
        w.WriteAttribute(ValueTagKeyword, "media-supported", "iso_a4_210x297mm");
        w.WriteAttributeAdditional(ValueTagKeyword, "na_letter_8.5x11in");

        // Форматы документов. PDF стоит первым и по умолчанию — iOS будет отдавать приоритет ему.
        // image/urf ОБЯЗАТЕЛЕН: без него iOS не воспринимает принтер как AirPrint-совместимый.
        w.WriteAttribute(ValueTagMimeType, "document-format-default", "application/pdf");
        w.WriteAttribute(ValueTagMimeType, "document-format-supported", "application/pdf");
        w.WriteAttributeAdditional(ValueTagMimeType, "image/urf");

        // Обязательное подтверждение URF для AirPrint (без SRGB24 — принтер монохромный).
        w.WriteAttribute(ValueTagKeyword, "urf-supported", "V1.4");
        w.WriteAttributeAdditional(ValueTagKeyword, "CP1");
        w.WriteAttributeAdditional(ValueTagKeyword, "W8");
        w.WriteAttributeAdditional(ValueTagKeyword, "RS600");
        w.WriteAttributeAdditional(ValueTagKeyword, "DM1");

        // Canon MF3010 — монохромный. Должно совпадать с Color=F в mDNS TXT.
        w.WriteIntAttribute("color-supported", ValueTagBoolean, 0);

        // Стороны: MF3010 без дуплекса — только one-sided
        w.WriteAttribute(ValueTagKeyword, "sides-supported", "one-sided");
        w.WriteAttribute(ValueTagKeyword, "sides-default", "one-sided");

        // Финишинг: 3 = none (стандартный enum IPP)
        w.WriteIntAttribute("finishings-default", ValueTagEnum, 3);
        w.WriteIntAttribute("finishings-supported", ValueTagEnum, 3);

        // Копии: поддерживается диапазон 1-99, по умолчанию 1
        w.WriteIntAttribute("copies-default", ValueTagInteger, 1);
        w.WriteRangeAttribute(0x33, "copies-supported", 1, 99);

        // Качество печати: 3=draft, 4=normal, 5=high; по умолчанию normal
        w.WriteIntAttribute("print-quality-default", ValueTagEnum, 4);
        w.WriteIntAttribute("print-quality-supported", ValueTagEnum, 3);
        w.WriteIntAttributeAdditional(ValueTagEnum, 4);
        w.WriteIntAttributeAdditional(ValueTagEnum, 5);

        w.WriteAttribute(ValueTagKeyword, "pdl-override-supported", "attempted");
        w.WriteIntAttribute("printer-is-accepting-jobs", ValueTagBoolean, 1);
        w.WriteIntAttribute("queued-job-count", ValueTagInteger, 0);

        w.WriteByte(TagEndOfAttribs);
        return w.ToArray();
    }

    // Вспомогательный метод для записи resolution
    private void WriteResolutionAttribute(IppWriter w, string name, int xRes, int yRes, int units)
    {
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        w.WriteByte(ValueTagResolution);
        w.WriteShort((short)nameBytes.Length);       
        w.Write(nameBytes);        
        w.WriteShort(9); // resolution всегда 9 байт
        w.WriteInt(xRes);
        w.WriteInt(yRes);
        w.WriteByte((byte)units); // 3 = dots per inch
    }

    // Добавь метод для получения локального IP
    private string GetLocalIp()
    {
        return MulticastService.GetIPAddresses()
            .FirstOrDefault(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                                 && x.ToString().StartsWith("192.168.0"))
            ?.ToString() ?? "192.168.0.107";
    }

    // Заменяем заглушку BuildPrintJobResponse на реальную обработку
    private async Task<byte[]> HandlePrintJob(HttpListenerRequest req, int requestId, byte[] rawIppData)
    {
        try
        {
            // IPP пакет: заголовок 8 байт + атрибуты до тега 0x03 + затем данные документа
            // Ищем тег конца атрибутов (0x03) — после него идёт PDF
            int documentOffset = FindDocumentOffset(rawIppData);

            if (documentOffset < 0 || documentOffset >= rawIppData.Length)
            {
                _logger.LogWarning("Print-Job: document data not found in IPP packet");
                return BuildPrintJobResponse(requestId, success: false);
            }

            var documentData = rawIppData[documentOffset..];

            // Определяем формат документа из IPP атрибутов
            var documentFormat = ExtractDocumentFormat(rawIppData, documentOffset);
            var jobName = ExtractJobName(rawIppData, documentOffset);

            _logger.LogInformation(
                "Print-Job: format={Format}, size={Size} bytes, job='{Name}'",
                documentFormat, documentData.Length, jobName);

            // Сохраняем для диагностики (пока разрабатываем)
            var debugPath = Path.Combine(Path.GetTempPath(), $"airprint_job_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
            await File.WriteAllBytesAsync(debugPath, documentData);
            _logger.LogInformation("Debug: document saved to {Path}", debugPath);

            // Отправляем на печать
            await _printDispatcher.PrintAsync(documentData, documentFormat, jobName);

            return BuildPrintJobResponse(requestId, success: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process Print-Job");
            return BuildPrintJobResponse(requestId, success: false);
        }
    }
    /*
    private byte[] BuildPrintJobResponse(int requestId)
    {
        _logger.LogInformation("*** Print-Job received! (stub - will implement printing later)");

        var w = new IppWriter();
        w.WriteVersion(VersionIpp11);
        w.WriteShort(StatusOk);
        w.WriteInt(requestId);

        w.WriteByte(TagOperationAttribs);
        w.WriteAttribute(ValueTagCharset, "attributes-charset", "utf-8");
        w.WriteAttribute(ValueTagNatLang, "attributes-natural-language", "en");

        // Job attributes group — возвращаем фиктивный job-id
        w.WriteByte(0x05); // job-attributes-tag
        w.WriteIntAttribute("job-id", ValueTagInteger, 1);
        w.WriteAttribute(ValueTagUri, "job-uri", $"ipp://localhost:{_config.IppPort}/jobs/1");
        w.WriteIntAttribute("job-state", ValueTagEnum, 9); // 9 = completed

        w.WriteByte(TagEndOfAttribs);
        return w.ToArray();
    }*/

    private byte[] BuildPrintJobResponse(int requestId, bool success = true)
    {
        var w = new IppWriter();
        w.WriteVersion(VersionIpp11);
        w.WriteShort(success ? StatusOk : (short)0x0500); // 0x0500 = server-error-internal-error
        w.WriteInt(requestId);

        w.WriteByte(TagOperationAttribs);
        w.WriteAttribute(ValueTagCharset, "attributes-charset", "utf-8");
        w.WriteAttribute(ValueTagNatLang, "attributes-natural-language", "en");

        if (success)
        {
            w.WriteByte(0x05); // job-attributes-tag
            w.WriteIntAttribute("job-id", ValueTagInteger, 1);

            // ИСПРАВЛЕНИЕ 4: Избавляемся от localhost и здесь
            var hostName = "AirPrint-Bridge-Server.local";
            w.WriteAttribute(ValueTagUri, "job-uri", $"ipp://{hostName}:{_config.IppPort}/jobs/1");

            // job-state: 3=pending, 4=pending-held, 5=processing, 9=completed
            w.WriteIntAttribute("job-state", ValueTagEnum, 9);
            w.WriteAttribute(ValueTagKeyword, "job-state-reasons", "job-completed-successfully");
        }

        w.WriteByte(TagEndOfAttribs);
        return w.ToArray();
    }

    private byte[] BuildSimpleSuccessResponse(int requestId)
    {
        var w = new IppWriter();
        w.WriteVersion(VersionIpp11);
        w.WriteShort(StatusOk);
        w.WriteInt(requestId);
        w.WriteByte(TagOperationAttribs);
        w.WriteAttribute(ValueTagCharset, "attributes-charset", "utf-8");
        w.WriteAttribute(ValueTagNatLang, "attributes-natural-language", "en");
        w.WriteByte(TagEndOfAttribs);
        return w.ToArray();
    }

    private static async Task HandleHttpRequest(HttpListenerRequest req, HttpListenerResponse resp)
    {
        // Простая диагностическая страница — открой в браузере http://localhost:631/
        var html = """
            <html><body>
            <h2>AirPrint Bridge is running</h2>
            <p>This service makes your Windows printer available to Apple devices via AirPrint.</p>
            <p>IPP endpoint: <code>/printers/canon</code></p>
            </body></html>
            """;
        var bytes = System.Text.Encoding.UTF8.GetBytes(html);
        resp.ContentType = "text/html";
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes);
    }

    private static string GetOperationName(short op) => op switch
    {
        0x0002 => "Print-Job",
        0x0004 => "Validate-Job",
        0x000A => "Get-Jobs",
        0x000B => "Get-Printer-Attributes",
        0x0008 => "Cancel-Job",
        _ => $"Unknown(0x{op:X4})"
    };

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping IPP server");
        _cts?.Cancel();
        _listener?.Stop();
        if (_listenerTask != null)
            await _listenerTask.ConfigureAwait(false);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return StartAsync(stoppingToken);
    }

    /// <summary>
    /// Корректно ищет конец IPP-атрибутов (тег 0x03) путём структурного обхода пакета.
    /// Наивный поиск байта 0x03 ненадёжен: этот байт встречается внутри значений
    /// атрибутов (например, printer-state=idle=3 кодируется как 0x00 0x00 0x00 0x03).
    ///
    /// IPP структура (RFC 8011):
    ///   Заголовок: version(2) + op(2) + requestId(4) = 8 байт
    ///   Затем чередуются:
    ///     group-tag (0x01..0x0F) — начало новой группы атрибутов
    ///     end-of-attributes-tag (0x03) — конец всех атрибутов, далее идут данные
    ///     attribute-entry (0x10..0xFF) — тег значения, затем:
    ///       name-length(2) + name(name-length) + value-length(2) + value(value-length)
    ///       Если name-length == 0 — это дополнительное значение (additional value)
    /// </summary>
    private static int FindDocumentOffset(byte[] data)
    {
        if (data.Length < 8) return -1;

        int pos = 8; // пропускаем 8-байтный IPP-заголовок

        while (pos < data.Length)
        {
            byte tag = data[pos];

            // Тег конца атрибутов (end-of-attributes-tag)
            if (tag == 0x03)
                return pos + 1; // документ начинается сразу после этого байта

            // Group delimiter tags (0x01..0x0F, кроме 0x03) — просто шагаем дальше
            if (tag >= 0x01 && tag <= 0x0F)
            {
                pos++;
                continue;
            }

            // Атрибут: value-tag (0x10..0xFF)
            // Формат: tag(1) + name-length(2) + name(N) + value-length(2) + value(M)
            if (pos + 3 > data.Length) break;

            int nameLen = ((data[pos + 1] & 0xFF) << 8) | (data[pos + 2] & 0xFF);
            pos += 3 + nameLen; // пропускаем tag + name-length + name

            if (pos + 2 > data.Length) break;
            int valueLen = ((data[pos] & 0xFF) << 8) | (data[pos + 1] & 0xFF);
            pos += 2 + valueLen; // пропускаем value-length + value
        }

        return -1; // тег 0x03 не найден
    }

    /// <summary>
    /// Извлекаем document-format из IPP атрибутов.
    /// Ищем ASCII строку "document-format" и берём значение после неё.
    /// Это упрощённый парсинг — в следующей итерации заменим на полноценный.
    /// </summary>
    private static string ExtractDocumentFormat(byte[] data, int endOffset)
    {
        // Ищем имя атрибута "document-format" как байтовую последовательность
        var marker = "document-format"u8.ToArray();
        var span = data.AsSpan(0, endOffset);

        for (int i = 0; i < span.Length - marker.Length; i++)
        {
            if (span.Slice(i, marker.Length).SequenceEqual(marker))
            {
                // После имени атрибута идёт [2 bytes value-length][value]
                int valueStart = i + marker.Length;
                if (valueStart + 2 >= endOffset) break;
                int valueLen = (span[valueStart] << 8) | span[valueStart + 1];
                if (valueStart + 2 + valueLen > endOffset) break;
                return System.Text.Encoding.UTF8.GetString(span.Slice(valueStart + 2, valueLen));
            }
        }
        return "application/pdf"; // по умолчанию
    }

    private static string ExtractJobName(byte[] data, int endOffset)
    {
        var marker = "job-name"u8.ToArray();
        var span = data.AsSpan(0, endOffset);

        for (int i = 0; i < span.Length - marker.Length; i++)
        {
            if (span.Slice(i, marker.Length).SequenceEqual(marker))
            {
                int valueStart = i + marker.Length;
                if (valueStart + 2 >= endOffset) break;
                int valueLen = (span[valueStart] << 8) | span[valueStart + 1];
                if (valueStart + 2 + valueLen > endOffset) break;
                return System.Text.Encoding.UTF8.GetString(span.Slice(valueStart + 2, valueLen));
            }
        }
        return $"AirPrint Job {DateTime.Now:HH:mm:ss}";
    }

    private byte[] BuildGetJobsResponse(int requestId)
    {
        var w = new IppWriter();
        w.WriteVersion(VersionIpp11);
        w.WriteShort(StatusOk);
        w.WriteInt(requestId);

        w.WriteByte(TagOperationAttribs);
        w.WriteAttribute(ValueTagCharset, "attributes-charset", "utf-8");
        w.WriteAttribute(ValueTagNatLang, "attributes-natural-language", "en");
        // Пустой список заданий
        w.WriteByte(TagEndOfAttribs);
        return w.ToArray();
    }


}