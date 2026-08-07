// Fase 70.4-E — capa de API tipada para Wallet Caja/Cuadre Comercio.
// Espejo exacto de los DTOs reales del backend (WalletCajaComercioDtos.cs,
// ComercioScopeDtos.cs). No se inventan campos ni endpoints.
import { get, post, patch } from './client.ts';

export interface ComercioScope {
  idUsuario:                  number;
  rolComercio:                string;
  idComercioAliado:           number;
  idComercioExistente?:       number;
  idEstablecimiento?:         number;
  puedeVerTodoComercio:       boolean;
  puedeDisponerRecursos:      boolean;
  puedeLiquidarAnticipado:    boolean;
  puedeEnviarBreb:            boolean;
  puedeAnularVentasDiaActual: boolean;
  puedeGenerarQr:             boolean;
}

export interface CajaAccionesDisponibles {
  puedeIniciarCuadre:        boolean;
  puedeCerrar:                boolean;
  puedeRevisar:               boolean;
  puedeVerComprobante:        boolean;
  puedeCorregirFondoInicial:  boolean;
}

export interface CajaDto {
  idCaja:                  number;
  idComercio:               number;
  nombreComercio:           string | null;
  idComercioAliado:         number | null;
  idEstablecimiento:        number;
  nombreEstablecimiento:    string | null;
  idUsuarioCajero:          number;
  nombreUsuarioCajero:      string | null;
  fechaOperativa:           string;
  fechaAperturaUtc:         string;
  fechaCierreUtc:           string | null;
  estado:                   string;
  fondoInicial:             number;
  efectivoEsperado:         number | null;
  efectivoContado:          number | null;
  diferencia:               number | null;
  tipoDiferencia:           string | null;
  cerradaAutomaticamente:   boolean;
  observacionesCajero:      string | null;
  revisadoPorUsuario:       number | null;
  nombreRevisor:            string | null;
  fechaRevision:            string | null;
  observacionesRevision:    string | null;
  acciones:                 CajaAccionesDisponibles;
}

export interface CajaResumenDto {
  idCaja:                number;
  idEstablecimiento:      number;
  nombreEstablecimiento:  string | null;
  idUsuarioCajero:        number;
  nombreUsuarioCajero:    string | null;
  fechaOperativa:         string;
  estado:                 string;
  fondoInicial:           number;
  efectivoEsperado:       number | null;
  efectivoContado:        number | null;
  diferencia:             number | null;
  tipoDiferencia:         string | null;
}

export interface PaginadoDto<T> {
  items:       T[];
  page:        number;
  pageSize:    number;
  totalItems:  number;
  totalPages:  number;
}

interface ApiEnvelope<T> {
  success:  boolean;
  message?: string;
  data?:    T;
}

export async function getMiScope(): Promise<ComercioScope | null> {
  const r = await get<ApiEnvelope<ComercioScope | null>>('/api/comercio/mi-scope');
  return r.data ?? null;
}

export async function getMiCajaActual(): Promise<CajaDto | null> {
  const r = await get<ApiEnvelope<CajaDto | null>>('/api/comercio/cajas/mi-caja-actual');
  return r.data ?? null;
}

export async function abrirCaja(fondoInicial: number, idEstablecimiento?: number): Promise<CajaDto> {
  const r = await post<ApiEnvelope<CajaDto>>('/api/comercio/cajas/abrir', {
    fondoInicial,
    ...(idEstablecimiento != null ? { idEstablecimiento } : {}),
  });
  if (!r.data) throw new Error(r.message ?? 'Respuesta inválida al abrir la caja.');
  return r.data;
}

export async function corregirFondoInicial(idCaja: number, fondoInicial: number, motivo: string): Promise<CajaDto> {
  const r = await patch<ApiEnvelope<CajaDto>>(`/api/comercio/cajas/${idCaja}/fondo-inicial`, { fondoInicial, motivo });
  if (!r.data) throw new Error(r.message ?? 'Respuesta inválida al corregir el fondo inicial.');
  return r.data;
}

export async function iniciarCuadre(idCaja: number): Promise<CajaDto> {
  const r = await post<ApiEnvelope<CajaDto>>(`/api/comercio/cajas/${idCaja}/iniciar-cuadre`, {});
  if (!r.data) throw new Error(r.message ?? 'Respuesta inválida al iniciar el cuadre.');
  return r.data;
}

export async function cerrarCaja(idCaja: number, efectivoContado: number, observaciones?: string): Promise<CajaDto> {
  const r = await post<ApiEnvelope<CajaDto>>(`/api/comercio/cajas/${idCaja}/cerrar`, {
    efectivoContado,
    ...(observaciones ? { observaciones } : {}),
  });
  if (!r.data) throw new Error(r.message ?? 'Respuesta inválida al cerrar la caja.');
  return r.data;
}

// Observaciones es obligatorio en este endpoint (WalletCajaComercioService.
// RevisarAsync lo exige explícitamente, aunque el DTO lo declare nullable).
export async function revisarCaja(idCaja: number, observaciones: string): Promise<CajaDto> {
  const r = await post<ApiEnvelope<CajaDto>>(`/api/comercio/cajas/${idCaja}/revisar`, { observaciones });
  if (!r.data) throw new Error(r.message ?? 'Respuesta inválida al revisar la caja.');
  return r.data;
}

export interface ListarCajasParams {
  page?:              number;
  pageSize?:          number;
  idEstablecimiento?: number;
  estado?:            string;
  desde?:             string;
  hasta?:             string;
}

export async function listarCajas(params: ListarCajasParams = {}): Promise<PaginadoDto<CajaResumenDto>> {
  const qs = new URLSearchParams();
  if (params.page != null)              qs.set('page', String(params.page));
  if (params.pageSize != null)          qs.set('pageSize', String(params.pageSize));
  if (params.idEstablecimiento != null) qs.set('idEstablecimiento', String(params.idEstablecimiento));
  if (params.estado)                    qs.set('estado', params.estado);
  if (params.desde)                     qs.set('desde', params.desde);
  if (params.hasta)                     qs.set('hasta', params.hasta);
  const suffix = qs.toString() ? `?${qs.toString()}` : '';
  const r = await get<ApiEnvelope<PaginadoDto<CajaResumenDto>>>(`/api/comercio/cajas${suffix}`);
  if (!r.data) throw new Error(r.message ?? 'Respuesta inválida al listar cajas.');
  return r.data;
}
