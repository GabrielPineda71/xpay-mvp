using Xpay.Api.Integrations.Passport;
using Xunit;

namespace Xpay.Api.Tests.Integrations.Passport;

// XPAY-594 — pruebas de VECTORES FIJOS de la canonicalización exigida por el
// esquema "X-Passport-Signature-V2".
//
// OFFICIAL_HMAC_TEST_VECTOR_AVAILABLE = NO. La documentación de Passport
// describe el algoritmo pero NO publica ningún vector con una firma calculada.
// Por tanto todos los vectores de este archivo son
// XPAY_INTERNAL_TEST_VECTOR: se generaron ejecutando la implementación de
// REFERENCIA que el propio contrato cita —
// json.dumps(payload, sort_keys=True, separators=(',', ':')) de Python — y se
// fijaron aquí como literales. NO deben presentarse como evidencia del
// proveedor.
//
// Su valor es de regresión: si la canonicalización .NET se desvía un solo
// carácter del comportamiento de Python, estos tests fallan antes de que una
// firma real pueda fallar en Sandbox.
public class PassportWebhookCanonicalJsonTests
{
    // Vector 1 — objeto simple.
    [Fact]
    public void Vector01_ObjetoSimple()
        => Assert.Equal(
            "{\"a\":\"1\",\"b\":\"2\"}",
            PassportWebhookCanonicalJson.Canonicalize("{\"b\":\"2\",\"a\":\"1\"}"));

    // Vector 2 — claves fuera de orden, incluida una MAYÚSCULA: el orden es por
    // punto de código, así que "Event" (E=0x45) precede a cualquier minúscula.
    [Fact]
    public void Vector02_ClavesFueraDeOrden_OrdenPorPuntoDeCodigo()
        => Assert.Equal(
            "{\"Event\":\"e\",\"a\":\"1\",\"z\":\"2\"}",
            PassportWebhookCanonicalJson.Canonicalize("{\"z\":\"2\",\"a\":\"1\",\"Event\":\"e\"}"));

    // Vector 3 — objetos anidados, ordenados en cada nivel.
    [Fact]
    public void Vector03_ObjetosAnidados()
        => Assert.Equal(
            "{\"a\":\"top\",\"z\":{\"x\":{\"w\":\"0\"},\"y\":\"1\"}}",
            PassportWebhookCanonicalJson.Canonicalize(
                "{\"z\":{\"y\":\"1\",\"x\":{\"w\":\"0\"}},\"a\":\"top\"}"));

    // Vector 4 — arrays: conservan su orden original; los objetos internos se
    // ordenan. Cubre también true/false/null.
    [Fact]
    public void Vector04_Arrays_ConservanOrden_YObjetosInternosSeOrdenan()
        => Assert.Equal(
            "{\"k\":\"v\",\"list\":[{\"a\":1,\"b\":2},\"s\",true,false,null]}",
            PassportWebhookCanonicalJson.Canonicalize(
                "{\"list\":[{\"b\":2,\"a\":1},\"s\",true,false,null],\"k\":\"v\"}"));

    // Vector 5 — espacios DENTRO de los valores se preservan intactos; sólo se
    // eliminan los espacios de formato entre tokens.
    [Fact]
    public void Vector05_EspaciosEnValores_SePreservan()
        => Assert.Equal(
            "{\"name\":\"  con  espacios  \",\"t\":\"a\\tb\\nc\"}",
            PassportWebhookCanonicalJson.Canonicalize(
                "{\"name\":\"  con  espacios  \",  \"t\": \"a\\tb\\nc\"}"));

    // Vector 6 — no ASCII: tildes y "ñ" se escapan como \uXXXX en MINÚSCULA
    // (ensure_ascii=True). Es el caso que más probablemente aparezca en
    // producción, con nombres colombianos.
    [Fact]
    public void Vector06_NoAscii_SeEscapaEnMinusculas()
        => Assert.Equal(
            "{\"city\":\"Bogot\\u00e1\",\"nombre\":\"MU\\u00d1OZ \\u00c1\\u00c9\\u00cd\\u00d3\\u00da \\u00f1\"}",
            PassportWebhookCanonicalJson.Canonicalize(
                "{\"nombre\":\"MUÑOZ ÁÉÍÓÚ ñ\",\"city\":\"Bogotá\"}"));

    // Vector 7 — enteros: se conservan literales, sin punto decimal.
    [Fact]
    public void Vector07_Enteros()
        => Assert.Equal(
            "{\"big\":1234567890123,\"n\":0,\"neg\":-42}",
            PassportWebhookCanonicalJson.Canonicalize(
                "{\"n\":0,\"big\":1234567890123,\"neg\":-42}"));

    // Vector 8 — flotantes con el repr() de Python, incluido el umbral exacto
    // de notación exponencial: 1e15 se escribe en forma fija y 1e16 en
    // exponencial. Es la divergencia concreta frente al formato "R" de .NET.
    [Fact]
    public void Vector08_Flotantes_ReprDePython()
        => Assert.Equal(
            "{\"a\":1.0,\"b\":0.5,\"c\":1.25,\"d\":1e+16,\"e\":1000000000000000.0," +
            "\"f\":1e-05,\"g\":0.0001,\"h\":-0.0}",
            PassportWebhookCanonicalJson.Canonicalize(
                "{\"a\":1.0,\"b\":0.5,\"c\":1.25,\"d\":1e16,\"e\":1e15," +
                "\"f\":1e-5,\"g\":0.0001,\"h\":-0.0}"));

    [Theory]
    [InlineData(1.0, "1.0")]
    [InlineData(0.5, "0.5")]
    [InlineData(1.25, "1.25")]
    [InlineData(1e16, "1e+16")]
    [InlineData(1e15, "1000000000000000.0")]
    [InlineData(1e-5, "1e-05")]
    [InlineData(0.0001, "0.0001")]
    [InlineData(-1.5, "-1.5")]
    [InlineData(0.0, "0.0")]
    public void Vector08b_FormatoDeFlotantes(double valor, string esperado)
        => Assert.Equal(esperado, PassportWebhookCanonicalJson.FormatPythonFloat(valor));

    // Vector 9 — vacíos: objeto, array y cadena.
    [Fact]
    public void Vector09_Vacios()
        => Assert.Equal(
            "{\"a\":[],\"o\":{},\"s\":\"\"}",
            PassportWebhookCanonicalJson.Canonicalize("{\"o\":{},\"a\":[],\"s\":\"\"}"));

    // Vector 10 — escapes: comilla y barra invertida se escapan; la barra
    // inclinada NO; el 0x7F sí, porque queda fuera de 0x20..0x7E.
    [Fact]
    public void Vector10_Escapes_BarraInclinadaNoSeEscapa_Y7fSi()
        => Assert.Equal(
            "{\"bs\":\"a\\\\b\",\"del\":\"x\\u007fy\",\"q\":\"say \\\"hi\\\"\",\"slash\":\"a/b\"}",
            PassportWebhookCanonicalJson.Canonicalize(
                "{\"q\":\"say \\\"hi\\\"\",\"bs\":\"a\\\\b\",\"slash\":\"a/b\",\"del\":\"x\u007fy\"}"));

    // Vector 11 — payload con la forma real de payment.inbound.received.
    // Canonical y firma generados con la implementación de referencia.
    internal const string InboundPayloadRaw =
        "{\n" +
        "  \"Event\": \"payment.inbound.received\",\n" +
        "  \"status\": \"PROCESSING\",\n" +
        "  \"amount\": { \"value\": \"1.00\", \"currency\": \"COP\" },\n" +
        "  \"receiver\": { \"owner\": { \"name\": \"MUÑOZ ÁLVAREZ\" } },\n" +
        "  \"payment_id\": \"9ff381d9-8b8e-4d20-b2e2-e1d0dd22d3d2\",\n" +
        "  \"created_at\": \"2026-03-10T17:44:52.111820Z\",\n" +
        "  \"updated_at\": \"2026-03-10T17:44:52.111820Z\"\n" +
        "}";

    internal const string InboundPayloadCanonical =
        "{\"Event\":\"payment.inbound.received\",\"amount\":{\"currency\":\"COP\",\"value\":\"1.00\"}," +
        "\"created_at\":\"2026-03-10T17:44:52.111820Z\"," +
        "\"payment_id\":\"9ff381d9-8b8e-4d20-b2e2-e1d0dd22d3d2\"," +
        "\"receiver\":{\"owner\":{\"name\":\"MU\\u00d1OZ \\u00c1LVAREZ\"}}," +
        "\"status\":\"PROCESSING\",\"updated_at\":\"2026-03-10T17:44:52.111820Z\"}";

    [Fact]
    public void Vector11_PayloadEntranteReal_FormaCanonica()
        => Assert.Equal(
            InboundPayloadCanonical,
            PassportWebhookCanonicalJson.Canonicalize(InboundPayloadRaw));

    // La canonicalización es idempotente: aplicarla a su propia salida no la
    // cambia. Garantiza que no dependemos del formato de entrada.
    [Fact]
    public void Canonicalizacion_EsIdempotente()
    {
        var una = PassportWebhookCanonicalJson.Canonicalize(InboundPayloadRaw);
        Assert.Equal(una, PassportWebhookCanonicalJson.Canonicalize(una));
    }

    [Fact]
    public void OrdenPorPuntoDeCodigo_NoEsOrdenOrdinalUtf16()
    {
        // U+1F600 (par suplente D83D DE00) vs U+FF21. Por punto de código
        // 0x1F600 > 0xFF21; por unidad UTF-16, 0xD83D < 0xFF21. Este test fija
        // el criterio correcto (punto de código, como sorted() de Python).
        var suplente = char.ConvertFromUtf32(0x1F600);
        var bmpAlto  = "\uFF21";
        Assert.True(PassportWebhookCanonicalJson.CompareByCodePoint(bmpAlto, suplente) < 0);
        Assert.True(string.CompareOrdinal(bmpAlto, suplente) > 0);
    }
}
