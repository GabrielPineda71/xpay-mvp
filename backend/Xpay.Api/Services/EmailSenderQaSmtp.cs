using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using Xpay.Api.Common;

namespace Xpay.Api.Services;

// QA-IDENTIDAD-EMAIL-3 — sender SMTP dedicado EXCLUSIVAMENTE a QA (cuenta de
// correo propia de QA, nunca la de producción). Solo lo registra
// EmailSenderRegistro cuando el ambiente es exactamente "QA" y Email:Modo es
// exactamente "QaSmtp"; Development, Test y cualquier ambiente productivo no
// pueden construirlo. El buzón es correo REAL (no sandbox): el destinatario se
// valida contra una allowlist de DIRECCIONES EXACTAS ANTES de abrir red
// (R3: nunca por dominio, subdominio ni comodín).
// Nunca registra cuerpo, token, enlace, credenciales ni destinatario completo,
// y los errores del proveedor se sustituyen por un mensaje fijo sin causa
// interna.
public sealed class EmailSenderQaSmtp : IEmailSender
{
    internal const string PrefijoAsunto = "[QA] ";

    private readonly OpcionesEmailQaSmtp _opciones;
    private readonly ITransporteSmtpQa _transporte;
    private readonly ILogger<EmailSenderQaSmtp> _logger;

    internal EmailSenderQaSmtp(OpcionesEmailQaSmtp opciones, ITransporteSmtpQa transporte, ILogger<EmailSenderQaSmtp> logger)
    {
        _opciones   = opciones ?? throw new ArgumentNullException(nameof(opciones));
        _transporte = transporte ?? throw new ArgumentNullException(nameof(transporte));
        _logger     = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task EnviarAsync(MensajeEmail mensaje, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mensaje);
        ct.ThrowIfCancellationRequested();

        // Allowlist ANTES de abrir red. EmailNormalizador ya rechaza nombre
        // visible, listas, espacios y saltos de línea (inyección de cabeceras).
        if (!EmailNormalizador.TryNormalizar(mensaje.Destinatario, out var destinatario)
            || !_opciones.PermiteDestinatario(destinatario))
        {
            _logger.LogWarning("Email QA rechazado antes de abrir red: destinatario fuera de la allowlist de direcciones.");
            throw new EmailEnvioQaException(EmailEnvioQaException.MensajeDestinoNoPermitido);
        }

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(_opciones.Timeout);
        try
        {
            await _transporte.EnviarAsync(
                new EnvioSmtpQa(destinatario, PrefijoAsunto + mensaje.Asunto, mensaje.CuerpoTexto ?? string.Empty),
                limite.Token);
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // Cancelación del llamador: se propaga, sin el texto del proveedor.
            throw new OperationCanceledException(ct);
        }
        catch (Exception ex)
        {
            // Solo el tipo: el mensaje del proveedor puede citar direcciones o
            // fragmentos del correo. El tiempo agotado NO sale como
            // OperationCanceledException para que los servicios lo traten como
            // fallo de envío y no como cancelación de la petición.
            _logger.LogWarning("Fallo del transporte SMTP QA ({TipoError}).", ex.GetType().Name);
            throw new EmailEnvioQaException(limite.IsCancellationRequested
                ? EmailEnvioQaException.MensajeTiempoAgotado
                : EmailEnvioQaException.MensajeTransporte);
        }

        _logger.LogInformation(
            "Email QA entregado al servidor SMTP: destinatario {Destinatario}.",
            MensajeEmail.EnmascararDestinatario(destinatario));
    }
}

// Opciones validadas al ARRANQUE. Los secretos (usuario y clave SMTP) solo
// llegan por la configuración segura del entorno; ToString() nunca los muestra.
public sealed class OpcionesEmailQaSmtp
{
    public const string ClaveModo  = "Email:Modo";
    public const string ModoQaSmtp = "QaSmtp";
    public const string Seccion    = "Email:QaSmtp";

    public const string ClaveHost                    = Seccion + ":Host";
    public const string ClavePuerto                  = Seccion + ":Puerto";
    public const string ClaveUsuario                 = Seccion + ":Usuario";
    public const string ClaveClave                   = Seccion + ":Clave";
    public const string ClaveRemitente               = Seccion + ":Remitente";
    public const string ClaveDestinatariosPermitidos = Seccion + ":DestinatariosPermitidos";
    public const string ClaveTimeoutSegundos         = Seccion + ":TimeoutSegundos";

    // R3 — clave OBSOLETA: la allowlist por dominio no existe. Su sola
    // presencia (con cualquier valor) falla al arranque; nunca se ignora.
    public const string ClaveDominiosPermitidos = Seccion + ":DominiosPermitidos";

    // System.Net.Mail solo admite TLS explícito (STARTTLS); 465 es TLS implícito.
    internal const int PuertoTlsImplicito    = 465;
    internal const int TimeoutMaximoSegundos = 120;
    // Máximo de destinatarios ÚNICOS tras normalizar y eliminar duplicados.
    internal const int MaximoDestinatarios   = 10;

    internal OpcionesEmailQaSmtp(string host, int puerto, string usuario, string clave, string remitente,
        IReadOnlyList<string> destinatariosPermitidos, TimeSpan timeout)
    {
        Host                    = host;
        Puerto                  = puerto;
        Usuario                 = usuario;
        Clave                   = clave;
        Remitente               = remitente;
        DestinatariosPermitidos = destinatariosPermitidos;
        Timeout                 = timeout;
    }

    public string Host { get; }
    public int Puerto { get; }
    internal string Usuario { get; }
    internal string Clave { get; }
    public string Remitente { get; }
    internal IReadOnlyList<string> DestinatariosPermitidos { get; }
    public TimeSpan Timeout { get; }

    // Coincidencia EXACTA y ordinal de la dirección ya normalizada: sin
    // dominio, subdominio, comodín ni coincidencia parcial.
    internal bool PermiteDestinatario(string destinatarioNormalizado) =>
        DestinatariosPermitidos.Contains(destinatarioNormalizado, StringComparer.Ordinal);

    // Solo la cantidad de destinatarios: nunca las direcciones.
    public override string ToString() =>
        $"OpcionesEmailQaSmtp {{ Host = {Host}, Puerto = {Puerto}, Usuario = ***, Clave = ***, Remitente = {Remitente}, " +
        $"DestinatariosPermitidos = {DestinatariosPermitidos.Count}, TimeoutSegundos = {(int)Timeout.TotalSeconds} }}";

    // Falla cerrado ante cualquier valor ausente o inválido. Los mensajes
    // nombran la clave, nunca su valor.
    internal static OpcionesEmailQaSmtp Desde(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var host = configuration[ClaveHost]?.Trim();
        if (string.IsNullOrEmpty(host) || host.Length > 253 || Uri.CheckHostName(host) == UriHostNameType.Unknown)
            throw Invalida(ClaveHost);

        if (!int.TryParse(configuration[ClavePuerto], NumberStyles.None, CultureInfo.InvariantCulture, out var puerto)
            || puerto < 1 || puerto > 65535 || puerto == PuertoTlsImplicito)
            throw Invalida(ClavePuerto);

        var usuario = configuration[ClaveUsuario];
        if (string.IsNullOrWhiteSpace(usuario)) throw Invalida(ClaveUsuario);

        var clave = configuration[ClaveClave];
        if (string.IsNullOrEmpty(clave)) throw Invalida(ClaveClave);

        // Cuenta dedicada de QA: nunca los remitentes previstos para producción.
        if (!EmailNormalizador.TryNormalizar(configuration[ClaveRemitente], out var remitente)
            || remitente == EmailRemitentes.NoReply || remitente == EmailRemitentes.Soporte)
            throw Invalida(ClaveRemitente);

        if (configuration.GetSection(ClaveDominiosPermitidos).Exists())
            throw Invalida(ClaveDominiosPermitidos);

        // Lista de direcciones completas separadas por ',' o ';'. La forma de
        // array (sin valor escalar) se trata como ausente. Cualquier elemento
        // vacío o inválido invalida la lista entera; EmailNormalizador rechaza
        // nombre visible, espacios, saltos de línea y varias '@'.
        var destinatarios = new List<string>();
        foreach (var parte in (configuration[ClaveDestinatariosPermitidos] ?? string.Empty).Split(',', ';'))
        {
            if (parte.Contains('*') || !EmailNormalizador.TryNormalizar(parte, out var destinatario))
                throw Invalida(ClaveDestinatariosPermitidos);
            if (!destinatarios.Contains(destinatario, StringComparer.Ordinal)) destinatarios.Add(destinatario);
        }
        if (destinatarios.Count == 0 || destinatarios.Count > MaximoDestinatarios)
            throw Invalida(ClaveDestinatariosPermitidos);

        if (!int.TryParse(configuration[ClaveTimeoutSegundos], NumberStyles.None, CultureInfo.InvariantCulture, out var timeout)
            || timeout < 1 || timeout > TimeoutMaximoSegundos)
            throw Invalida(ClaveTimeoutSegundos);

        return new OpcionesEmailQaSmtp(host, puerto, usuario, clave, remitente, destinatarios, TimeSpan.FromSeconds(timeout));
    }

    private static EmailConfiguracionException Invalida(string clave) =>
        new("Falta o es inválida la configuración de email QA (" + clave + ").");
}

// Envío ya validado. ToString() oculta cuerpo y destinatario.
internal sealed record EnvioSmtpQa(string Destinatario, string Asunto, string CuerpoTexto)
{
    public override string ToString() =>
        $"EnvioSmtpQa {{ Destinatario = {MensajeEmail.EnmascararDestinatario(Destinatario)}, Asunto = {Asunto}, CuerpoTexto = *** }}";
}

// Costura de transporte: los tests la sustituyen y nunca abren red.
internal interface ITransporteSmtpQa
{
    Task EnviarAsync(EnvioSmtpQa envio, CancellationToken ct);
}

// SMTP autenticado con STARTTLS obligatorio (EnableSsl): si el servidor no
// ofrece TLS el envío falla; nunca degrada a texto plano. La validación del
// certificado es la del sistema (no se sobrescribe). Cliente nuevo por envío:
// SmtpClient no es seguro entre hilos y el sender es singleton.
internal sealed class TransporteSmtpQa(OpcionesEmailQaSmtp opciones) : ITransporteSmtpQa
{
    public async Task EnviarAsync(EnvioSmtpQa envio, CancellationToken ct)
    {
        using var mensaje = new MailMessage(new MailAddress(opciones.Remitente), new MailAddress(envio.Destinatario))
        {
            Subject         = envio.Asunto,
            SubjectEncoding = Encoding.UTF8,
            Body            = envio.CuerpoTexto,
            BodyEncoding    = Encoding.UTF8,
            IsBodyHtml      = false,
        };
        using var cliente = new SmtpClient(opciones.Host, opciones.Puerto)
        {
            DeliveryMethod        = SmtpDeliveryMethod.Network,
            EnableSsl             = true,
            UseDefaultCredentials = false,
            Credentials           = new NetworkCredential(opciones.Usuario, opciones.Clave),
            Timeout               = (int)opciones.Timeout.TotalMilliseconds,
        };
        await cliente.SendMailAsync(mensaje, ct);
    }
}

// Configuración de email inválida detectada al ARRANQUE (fail-closed). No
// deriva de InvalidOperationException; el mensaje nombra la clave, nunca su
// valor.
public sealed class EmailConfiguracionException : Exception
{
    public EmailConfiguracionException(string message) : base(message) { }
}

// Fallo de envío QA con mensaje fijo y SIN InnerException: el texto del
// proveedor SMTP puede contener direcciones o fragmentos del mensaje.
public sealed class EmailEnvioQaException : Exception
{
    public const string MensajeDestinoNoPermitido = "Destinatario no permitido por la allowlist de email QA.";
    public const string MensajeTransporte         = "No se pudo entregar el email QA al servidor SMTP.";
    public const string MensajeTiempoAgotado      = "Tiempo agotado al entregar el email QA al servidor SMTP.";

    public EmailEnvioQaException(string message) : base(message) { }
}
