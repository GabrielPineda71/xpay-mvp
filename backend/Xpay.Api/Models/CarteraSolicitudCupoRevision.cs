using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace Xpay.Api.Models;

// XPAY-702 - resolucion manual de una solicitud (migracion 046). Una fila por
// solicitud; se inserta una vez y no se edita. Conserva el resultado
// automatico original.
public class CarteraSolicitudCupoRevision
{
    public long      IdRevision                      { get; set; }
    public long      IdSolicitud                     { get; set; }
    public string    DecisionManual                  { get; set; } = string.Empty;
    public decimal?  MontoAprobadoManual             { get; set; }
    public string    ReglaAplicada                   { get; set; } = string.Empty;
    public string    MotivoCodigo                    { get; set; } = string.Empty;
    public string?   Observacion                     { get; set; }
    public long      IdAdmin                         { get; set; }
    public DateTime  FechaResolucionUtc              { get; set; }
    public string    DecisionAutomaticaOriginal      { get; set; } = string.Empty;
    public string    EstadoAutomaticoOriginal        { get; set; } = string.Empty;
    public string?   MotivoAutomaticoOriginal        { get; set; }
    public DateTime? FechaDecisionAutomaticaOriginal { get; set; }

    public static void Configurar(EntityTypeBuilder<CarteraSolicitudCupoRevision> e)
    {
        e.ToTable("cartera_solicitud_cupo_revisiones");
        e.HasKey(x => x.IdRevision);
        e.Property(x => x.IdRevision).HasColumnName("id_revision");
        e.Property(x => x.IdSolicitud).HasColumnName("id_solicitud");
        e.Property(x => x.DecisionManual).HasColumnName("decision_manual");
        e.Property(x => x.MontoAprobadoManual)
            .HasColumnName("monto_aprobado_manual").HasPrecision(18, 2);
        e.Property(x => x.ReglaAplicada).HasColumnName("regla_aplicada");
        e.Property(x => x.MotivoCodigo).HasColumnName("motivo_codigo");
        e.Property(x => x.Observacion).HasColumnName("observacion");
        e.Property(x => x.IdAdmin).HasColumnName("id_admin");
        e.Property(x => x.FechaResolucionUtc).HasColumnName("fecha_resolucion_utc");
        e.Property(x => x.DecisionAutomaticaOriginal)
            .HasColumnName("decision_automatica_original");
        e.Property(x => x.EstadoAutomaticoOriginal)
            .HasColumnName("estado_automatico_original");
        e.Property(x => x.MotivoAutomaticoOriginal)
            .HasColumnName("motivo_automatico_original");
        e.Property(x => x.FechaDecisionAutomaticaOriginal)
            .HasColumnName("fecha_decision_automatica_original");
    }
}
