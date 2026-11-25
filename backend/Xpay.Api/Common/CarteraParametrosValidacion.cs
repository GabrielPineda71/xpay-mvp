using Xpay.Api.DTOs;

namespace Xpay.Api.Common;

// Contrato técnico de almacenamiento. No establece precios, cupos ni topes
// comerciales: los valores siguen siendo una decisión del administrador.
public static class CarteraParametrosValidacion
{
    private const decimal MaximoPorcentajeSql = 99999.9999m; // decimal(9,4)
    private const decimal MaximoMontoSql = 9999999999999999.99m; // decimal(18,2)

    public static string Tipo(string? tipo)
    {
        var normalizado = tipo?.Trim().ToUpperInvariant();
        return normalizado is "COMPRA_COMERCIO" or "AVANCE_WALLET"
            ? normalizado
            : throw new ArgumentException("Selecciona Compra en comercio o Desembolso a Wallet.");
    }

    public static UpsertParametroUtilizacionRequest Validar(UpsertParametroUtilizacionRequest? req)
    {
        if (req is null) throw new ArgumentException("Completa todos los parámetros de la modalidad.");
        // El motor French actual obtiene 0/0 con tasa cero. Admitir créditos
        // sin intereses requiere su propia implementación; no se simula aquí.
        if (req.TasaEmv <= 0 || !PorcentajeSql(req.TasaEmv))
            throw new ArgumentException("La tasa EMV debe ser positiva, estar dentro del rango admitido y tener hasta cuatro decimales.");
        if (!PorcentajeSql(req.PorcAval) || !PorcentajeSql(req.PorcAdmin) || !PorcentajeSql(req.PorcIva))
            throw new ArgumentException("Revisa aval, administración e IVA: deben ser no negativos y tener rango y precisión admitidos.");
        if (req.PlazoMin < 1 || req.PlazoMax < req.PlazoMin)
            throw new ArgumentException("Los plazos deben ser meses enteros positivos y el máximo no puede ser menor que el mínimo.");
        // La frecuencia quincenal produce plazo * 2 cuotas. Es un límite de
        // representación de int, no una nueva política comercial de plazo.
        if (req.PlazoMax > int.MaxValue / 2)
            throw new ArgumentException("El plazo excede el máximo técnico admitido.");
        if (!MontoSql(req.MontoMin) || !MontoSql(req.MontoMax))
            throw new ArgumentException("Los montos deben ser no negativos, estar dentro del rango admitido y tener hasta dos decimales.");
        if (req.MontoMax <= 0 || req.MontoMax < req.MontoMin)
            throw new ArgumentException("El monto máximo debe ser positivo y no menor que el mínimo.");
        var frecuencia = req.Frecuencia?.Trim().ToUpperInvariant();
        if (frecuencia is not ("MENSUAL" or "QUINCENAL"))
            throw new ArgumentException("Selecciona frecuencia Mensual o Quincenal.");
        return req with { Frecuencia = frecuencia };
    }

    private static bool PorcentajeSql(decimal valor) =>
        valor >= 0 && valor <= MaximoPorcentajeSql && decimal.Round(valor, 4) == valor;

    private static bool MontoSql(decimal valor) =>
        valor >= 0 && valor <= MaximoMontoSql && decimal.Round(valor, 2) == valor;
}
