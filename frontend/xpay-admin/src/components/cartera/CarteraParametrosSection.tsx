import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { ApiError, get, post, put } from '../../api/client.ts';
import { useAuth } from '../../auth/AuthContext.tsx';
import './cartera-parametros.css';

const BASE = '/api/cartera-ordinaria/admin/parametros';
const TIPOS = ['COMPRA_COMERCIO', 'AVANCE_WALLET'] as const;
type Tipo = typeof TIPOS[number];
type Frecuencia = 'MENSUAL' | 'QUINCENAL';
const NOMBRES: Record<Tipo, string> = { COMPRA_COMERCIO: 'Compra en comercio', AVANCE_WALLET: 'Avance a wallet' };

interface Parametro {
  idParametro: number; tipoUtilizacion: Tipo; estado: 'ACTIVO' | 'INACTIVO';
  tasaEmv: number; porcAval: number; porcAdmin: number; aplicaIva: boolean; porcIva: number;
  plazoMin: number; plazoMax: number; frecuencia: Frecuencia; montoMin: number; montoMax: number;
}
type Body = Omit<Parametro, 'idParametro' | 'tipoUtilizacion' | 'estado'>;
type Campo = Exclude<keyof Body, 'aplicaIva'>;
type Formulario = Record<Campo, string> & { tipo: string; aplicaIva: '' | 'si' | 'no' };
type Editor = { modo: 'crear' | 'editar'; estado?: Parametro['estado']; id?: number; valores: Formulario };
type Lista = { estado: 'cargando' | 'lista' | 'error'; registros: Parametro[]; error?: string; sesion: string };
const VACIO: Formulario = {
  tipo: '', tasaEmv: '', porcAval: '', porcAdmin: '', aplicaIva: '', porcIva: '',
  plazoMin: '', plazoMax: '', frecuencia: '', montoMin: '', montoMax: '',
};
const NUMERICOS: [Campo, string, number][] = [
  ['tasaEmv', 'Tasa EMV (%)', 4], ['porcAval', 'Aval (%)', 4], ['porcAdmin', 'Administración (%)', 4],
  ['plazoMin', 'Plazo mínimo (meses)', 0], ['plazoMax', 'Plazo máximo (meses)', 0],
  ['montoMin', 'Monto mínimo (COP)', 2], ['montoMax', 'Monto máximo (COP)', 2],
];
const CAMPOS_BODY: (keyof Body)[] = ['tasaEmv', 'porcAval', 'porcAdmin', 'aplicaIva', 'porcIva', 'plazoMin', 'plazoMax', 'frecuencia', 'montoMin', 'montoMax'];
const esTipo = (v: unknown): v is Tipo => TIPOS.includes(v as Tipo);
const esFrecuencia = (v: unknown): v is Frecuencia => v === 'MENSUAL' || v === 'QUINCENAL';
const esNumero = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v);
function esParametro(v: unknown): v is Parametro {
  if (!v || typeof v !== 'object') return false;
  const p = v as Parametro;
  return Number.isSafeInteger(p.idParametro) && p.idParametro > 0 && esTipo(p.tipoUtilizacion)
    && (p.estado === 'ACTIVO' || p.estado === 'INACTIVO') && typeof p.aplicaIva === 'boolean'
    && esFrecuencia(p.frecuencia) && ['tasaEmv', 'porcAval', 'porcAdmin', 'porcIva', 'plazoMin', 'plazoMax', 'montoMin', 'montoMax']
      .every(k => esNumero(p[k as keyof Parametro])) && Number.isSafeInteger(p.plazoMin) && Number.isSafeInteger(p.plazoMax);
}
function esLista(v: unknown): v is Parametro[] {
  return Array.isArray(v) && v.every(esParametro) && new Set(v.map(p => p.tipoUtilizacion)).size === v.length;
}
// Cents remain safe integers and survive JSON's number representation unchanged.
function enteroEscalado(v: string, escala: number): bigint {
  const [entero, decimal = ''] = v.split('.');
  return BigInt(entero) * (10n ** BigInt(escala)) + BigInt(decimal.padEnd(escala, '0') || '0');
}
function validar(v: Formulario): { body?: Body; errores: Record<string, string> } {
  const errores: Record<string, string> = {};
  const numeros: Partial<Record<Campo, number>> = {};
  if (!esTipo(v.tipo)) errores.tipo = 'Selecciona un tipo disponible.';
  if (!esFrecuencia(v.frecuencia)) errores.frecuencia = 'Selecciona la frecuencia.';
  if (!v.aplicaIva) errores.aplicaIva = 'Selecciona si aplica IVA.';
  for (const [campo, label, escala] of [...NUMERICOS, ['porcIva', 'IVA (%)', 4] as [Campo, string, number]]) {
    const entrada = campo === 'porcIva' && v.aplicaIva === 'no' ? '0' : v[campo].trim().replace(',', '.');
    if (!entrada) { errores[campo] = `${label}: completa este campo.`; continue; }
    const valor = escala === 0 ? entrada : entrada.replace(/(\.\d*?)0+$/, '$1').replace(/\.$/, '');
    if (!/^\d+(?:\.\d+)?$/.test(entrada) || (valor.split('.')[1]?.length ?? 0) > escala) {
      errores[campo] = `${label}: usa un número no negativo con ${escala === 0 ? 'ningún decimal' : `máximo ${escala} decimales`}.`;
      continue;
    }
    const numero = Number(valor);
    const limite = escala === 4 ? 99999.9999 : escala === 0 ? 1073741823 : Number.MAX_SAFE_INTEGER / 100;
    if (!Number.isFinite(numero) || numero > limite || (escala === 2 && (
      enteroEscalado(valor, 2) > BigInt(Number.MAX_SAFE_INTEGER)
      || enteroEscalado(valor, 2) !== enteroEscalado(String(numero), 2)))) {
      errores[campo] = `${label}: el valor supera la precisión o el límite técnico permitido.`;
      continue;
    }
    if ((campo === 'tasaEmv' || campo === 'plazoMin' || campo === 'plazoMax' || campo === 'montoMax') && numero <= 0) {
      errores[campo] = `${label}: debe ser mayor que cero.`;
      continue;
    }
    numeros[campo] = numero;
  }
  if (numeros.plazoMin !== undefined && numeros.plazoMax !== undefined && numeros.plazoMin > numeros.plazoMax)
    errores.plazoMax = 'El plazo máximo debe ser mayor o igual al mínimo.';
  if (numeros.montoMin !== undefined && numeros.montoMax !== undefined && numeros.montoMin > numeros.montoMax)
    errores.montoMax = 'El monto máximo debe ser mayor o igual al mínimo.';
  if (Object.keys(errores).length) return { errores };
  return { errores, body: { ...numeros, aplicaIva: v.aplicaIva === 'si', frecuencia: v.frecuencia } as Body };
}
const fmt = (v: number) => new Intl.NumberFormat('es-CO', { maximumFractionDigits: 4 }).format(v);
const dinero = (v: number) => new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 2 }).format(v);

export function CarteraParametrosSection({ onBusyChange }: { onBusyChange?: (busy: boolean) => void }) {
  const { user } = useAuth();
  const autorizado = user?.roles.some(r => r === 'ADMIN_XPAY' || r === 'SUPERUSUARIO') === true;
  const sesion = user ? JSON.stringify([user.idUsuario, user.token, [...user.roles].sort()]) : '';
  const sesionActual = useRef(sesion);
  const versionSesion = useRef(0);
  if (sesionActual.current !== sesion) ++versionSesion.current;
  sesionActual.current = sesion;
  const version = versionSesion.current;
  const montado = useRef(false);
  const consulta = useRef(0);
  const escritura = useRef<string | null>(null);
  const callbackBusy = useRef(onBusyChange);
  callbackBusy.current = onBusyChange;
  const [lista, setLista] = useState<Lista>({ estado: 'cargando', registros: [], sesion });
  const [editor, setEditor] = useState<Editor | null>(null);
  const [errores, setErrores] = useState<Record<string, string>>({});
  const [guardando, setGuardando] = useState(false);
  const [mensaje, setMensaje] = useState<{ texto: string; tono: 'ok' | 'aviso' | 'error' } | null>(null);
  const vigente = useCallback(() => montado.current && sesionActual.current === sesion && versionSesion.current === version, [sesion, version]);
  const cargar = useCallback(async () => {
    if (!autorizado || !vigente()) return false;
    const solicitud = ++consulta.current;
    setLista({ estado: 'cargando', registros: [], sesion });
    try {
      const respuesta = await get<unknown>(BASE);
      if (!vigente() || solicitud !== consulta.current) return false;
      if (!esLista(respuesta)) throw new Error('La respuesta de parámetros no tiene el formato esperado.');
      setLista({ estado: 'lista', registros: respuesta, sesion });
      return true;
    } catch {
      if (vigente() && solicitud === consulta.current)
        setLista({ estado: 'error', registros: [], sesion, error: 'No se pudo consultar la lista de parámetros. Actualízala para continuar.' });
      return false;
    }
  }, [autorizado, sesion, vigente]);
  useEffect(() => {
    montado.current = true;
    escritura.current = null;
    setGuardando(false); setEditor(null); setErrores({}); setMensaje(null);
    callbackBusy.current?.(false);
    void cargar();
    return () => {
      montado.current = false;
      ++consulta.current;
      callbackBusy.current?.(false);
    };
  }, [cargar]);

  const listaLista = autorizado && lista.estado === 'lista' && lista.sesion === sesion;
  const disponibles = TIPOS.filter(t => !lista.registros.some(p => p.tipoUtilizacion === t));
  const abrirCreacion = () => {
    if (!listaLista || escritura.current || !disponibles.length) return;
    setEditor({ modo: 'crear', valores: { ...VACIO } }); setErrores({}); setMensaje(null);
  };
  const abrirEdicion = (p: Parametro) => {
    if (!listaLista || escritura.current) return;
    const valores = { ...VACIO, tipo: p.tipoUtilizacion, aplicaIva: p.aplicaIva ? 'si' : 'no' } as Formulario;
    for (const campo of CAMPOS_BODY) if (campo !== 'aplicaIva') valores[campo] = String(p[campo]);
    if (!p.aplicaIva) valores.porcIva = '0';
    setEditor({ modo: 'editar', estado: p.estado, id: p.idParametro, valores }); setErrores({}); setMensaje(null);
  };
  const cambiar = (campo: keyof Formulario, valor: string) => {
    if (escritura.current) return;
    setEditor(prev => prev ? { ...prev, valores: { ...prev.valores, [campo]: valor, ...(campo === 'aplicaIva' ? { porcIva: valor === 'no' ? '0' : '' } : {}) } } : null);
    setErrores(prev => { const nuevos = { ...prev }; delete nuevos[campo]; return nuevos; });
  };
  const guardar = async (e: FormEvent) => {
    e.preventDefault();
    if (!editor || !listaLista || escritura.current || !vigente()) return;
    const { body, errores: problemas } = validar(editor.valores);
    if (!body) { setErrores(problemas); return; }
    const tipo = editor.valores.tipo as Tipo;
    if (editor.modo === 'crear' && !disponibles.includes(tipo)) return;
    escritura.current = sesion;
    setGuardando(true); callbackBusy.current?.(true); setMensaje(null); setErrores({});
    try {
      const respuesta = await (editor.modo === 'crear' ? post<unknown>(`${BASE}/${tipo}`, body) : put<unknown>(`${BASE}/${tipo}`, body));
      if (!vigente()) return;
      if (!esParametro(respuesta) || respuesta.tipoUtilizacion !== tipo
        || respuesta.estado !== (editor.modo === 'crear' ? 'ACTIVO' : editor.estado)
        || (editor.modo === 'editar' && respuesta.idParametro !== editor.id)
        || CAMPOS_BODY.some(campo => respuesta[campo] !== body[campo])) throw new Error('Respuesta de guardado no confirmada.');
      setEditor(null);
      const actualizada = await cargar();
      if (!vigente()) return;
      setMensaje({ tono: actualizada ? 'ok' : 'aviso', texto: `${editor.modo === 'crear' ? 'Parámetros creados' : 'Cambios guardados'} correctamente.${actualizada ? '' : ' No se pudo consultar la lista; actualízala para continuar.'}` });
    } catch (error) {
      if (!vigente()) return;
      if (error instanceof ApiError && error.status >= 400 && error.status < 500 && error.status !== 409 && error.status !== 408) {
        setMensaje({ tono: 'error', texto: error.status === 401 || error.status === 403 ? 'La sesión no tiene autorización para guardar parámetros.' : `No se guardaron los parámetros. ${error.message}` });
      } else {
        setEditor(null);
        const conflicto = error instanceof ApiError && error.status === 409;
        setMensaje({ tono: 'aviso', texto: conflicto ? 'Este tipo ya está registrado. Consultando la lista…' : 'No se pudo confirmar el guardado. Consultando la lista sin repetir el envío…' });
        const actualizada = await cargar();
        if (!vigente()) return;
        setMensaje({ tono: 'aviso', texto: `${conflicto ? 'Este tipo ya está registrado; no se sobrescribió.' : 'No se pudo confirmar el guardado; no se repitió el envío.'} ${actualizada ? 'Consulta el estado en la lista antes de continuar.' : 'No se pudo consultar la lista. Actualízala antes de continuar.'}` });
      }
    } finally {
      if (vigente() && escritura.current === sesion) {
        escritura.current = null; setGuardando(false); callbackBusy.current?.(false);
      }
    }
  };
  if (!autorizado) return <p role="alert">Solo administradores y superusuarios pueden gestionar estos parámetros.</p>;

  return <section className="cartera-parametros" aria-labelledby="cartera-parametros-titulo">
    <div className="cartera-parametros__encabezado">
      <div><h3 id="cartera-parametros-titulo">Parámetros de utilización</h3><p>Configura las condiciones de cada tipo de utilización.</p></div>
      <div className="cartera-parametros__acciones">
        <button type="button" onClick={() => { setEditor(null); void cargar(); }} disabled={guardando || lista.estado === 'cargando'}>Actualizar lista</button>
        <button type="button" className="cartera-parametros__principal" onClick={abrirCreacion} disabled={!listaLista || !disponibles.length || guardando || !!editor}>Agregar parámetros</button>
      </div>
    </div>
    {mensaje && <p role={mensaje.tono === 'error' ? 'alert' : 'status'} className={`cartera-parametros__mensaje cartera-parametros__mensaje--${mensaje.tono}`}>{mensaje.texto}</p>}
    {(lista.estado === 'cargando' || lista.sesion !== sesion) && <p role="status">Cargando parámetros…</p>}
    {lista.estado === 'error' && lista.sesion === sesion && <p role="alert" className="cartera-parametros__mensaje cartera-parametros__mensaje--error">{lista.error}</p>}
    {listaLista && <>
      {lista.registros.length === 0 && <p className="cartera-parametros__vacio">Sin parámetros registrados. Agrega un tipo e ingresa las condiciones aprobadas.</p>}
      {!disponibles.length && <p>Los dos tipos ya están registrados. Puedes editar sus condiciones.</p>}
      <div className="cartera-parametros__tarjetas">{TIPOS.map(tipo => {
        const p = lista.registros.find(item => item.tipoUtilizacion === tipo);
        return <article className="cartera-parametros__tarjeta" key={tipo}>
          <div className="cartera-parametros__tarjeta-titulo"><h4>{NOMBRES[tipo]}</h4><span className={`cartera-parametros__estado${p?.estado === 'ACTIVO' ? ' cartera-parametros__estado--activo' : ''}`}>{p ? p.estado === 'ACTIVO' ? 'Activo' : 'Inactivo' : 'Sin registrar'}</span></div>
          <p className="cartera-parametros__codigo">{tipo}</p>
          {p ? <><dl>
            <div><dt>Tasa EMV</dt><dd>{fmt(p.tasaEmv)} %</dd></div><div><dt>Aval</dt><dd>{fmt(p.porcAval)} %</dd></div>
            <div><dt>Administración</dt><dd>{fmt(p.porcAdmin)} %</dd></div><div><dt>IVA</dt><dd>{p.aplicaIva ? `${fmt(p.porcIva)} %` : 'No aplica'}</dd></div>
            <div><dt>Plazo en meses</dt><dd>{p.plazoMin}–{p.plazoMax}</dd></div><div><dt>Frecuencia</dt><dd>{p.frecuencia === 'MENSUAL' ? 'Mensual' : 'Quincenal'}</dd></div>
            <div><dt>Monto mínimo</dt><dd>{dinero(p.montoMin)}</dd></div><div><dt>Monto máximo</dt><dd>{dinero(p.montoMax)}</dd></div>
          </dl><button type="button" onClick={() => abrirEdicion(p)} disabled={guardando || !!editor} aria-label={`Editar ${NOMBRES[tipo]}`}>Editar</button></> : <p>Disponible para agregar.</p>}
        </article>;
      })}</div>
    </>}
    {editor && listaLista && <form className="cartera-parametros__formulario" onSubmit={guardar} noValidate aria-labelledby="cartera-parametros-form-titulo">
      <h4 id="cartera-parametros-form-titulo">{editor.modo === 'crear' ? 'Agregar parámetros' : 'Editar parámetros'}</h4>
      <p>{editor.modo === 'crear' ? 'El nuevo registro quedará activo. ' : editor.estado === 'INACTIVO' ? 'Este registro seguirá inactivo al guardar. ' : ''}Estas condiciones aplican a nuevas simulaciones y utilizaciones. Guardarlas no asigna cupos ni activa la compra real; la pantalla de compra continúa en simulación.</p>
      {Object.keys(errores).length > 0 && <p role="alert" className="cartera-parametros__mensaje cartera-parametros__mensaje--error">Revisa los campos indicados antes de guardar.</p>}
      <fieldset disabled={guardando}>
        <legend className="cartera-parametros__sr">Condiciones de utilización</legend>
        <div className="cartera-parametros__grid">
          <label htmlFor="cartera-param-tipo">Tipo de utilización<select id="cartera-param-tipo" value={editor.valores.tipo} disabled={editor.modo === 'editar'} onChange={e => cambiar('tipo', e.target.value)} required aria-invalid={!!errores.tipo} aria-describedby={errores.tipo ? 'cartera-param-tipo-error' : undefined}>
            <option value="">Selecciona el tipo</option>{(editor.modo === 'crear' ? disponibles : [editor.valores.tipo as Tipo]).map(tipo => <option key={tipo} value={tipo}>{NOMBRES[tipo]} ({tipo})</option>)}
          </select>{errores.tipo && <span className="cartera-parametros__error" id="cartera-param-tipo-error">{errores.tipo}</span>}</label>
          <label htmlFor="cartera-param-frecuencia">Frecuencia registrada<select id="cartera-param-frecuencia" value={editor.valores.frecuencia} onChange={e => cambiar('frecuencia', e.target.value)} required aria-invalid={!!errores.frecuencia} aria-describedby={errores.frecuencia ? 'cartera-param-frecuencia-error' : 'cartera-param-frecuencia-ayuda'}>
            <option value="">Selecciona la frecuencia</option><option value="MENSUAL">Mensual</option><option value="QUINCENAL">Quincenal</option>
          </select>{errores.frecuencia && <span className="cartera-parametros__error" id="cartera-param-frecuencia-error">{errores.frecuencia}</span>}<span id="cartera-param-frecuencia-ayuda" className="cartera-parametros__ayuda">El simulador permite elegir mensual o quincenal.</span></label>
          {NUMERICOS.map(([campo, label, escala]) => <label key={campo} htmlFor={`cartera-param-${campo}`}>{label}<input id={`cartera-param-${campo}`} type="text" inputMode={escala ? 'decimal' : 'numeric'} autoComplete="off" value={editor.valores[campo]} onChange={e => cambiar(campo, e.target.value)} required aria-invalid={!!errores[campo]} aria-describedby={errores[campo] ? `cartera-param-${campo}-error` : undefined} />{errores[campo] && <span className="cartera-parametros__error" id={`cartera-param-${campo}-error`}>{errores[campo]}</span>}</label>)}
          <label htmlFor="cartera-param-aplicaIva">Aplica IVA<select id="cartera-param-aplicaIva" value={editor.valores.aplicaIva} onChange={e => cambiar('aplicaIva', e.target.value)} required aria-invalid={!!errores.aplicaIva} aria-describedby={errores.aplicaIva ? 'cartera-param-aplicaIva-error' : undefined}><option value="">Selecciona una opción</option><option value="si">Sí</option><option value="no">No</option></select>{errores.aplicaIva && <span className="cartera-parametros__error" id="cartera-param-aplicaIva-error">{errores.aplicaIva}</span>}</label>
          <label htmlFor="cartera-param-porcIva">IVA (%)<input id="cartera-param-porcIva" type="text" inputMode="decimal" autoComplete="off" value={editor.valores.porcIva} disabled={editor.valores.aplicaIva === 'no'} onChange={e => cambiar('porcIva', e.target.value)} required={editor.valores.aplicaIva !== 'no'} aria-invalid={!!errores.porcIva} aria-describedby={errores.porcIva ? 'cartera-param-porcIva-error' : 'cartera-param-iva-ayuda'} />{errores.porcIva && <span className="cartera-parametros__error" id="cartera-param-porcIva-error">{errores.porcIva}</span>}<span id="cartera-param-iva-ayuda" className="cartera-parametros__ayuda">Si no aplica IVA, el porcentaje es 0.</span></label>
        </div>
        <p className="cartera-parametros__ayuda">Todos los campos son obligatorios. Porcentajes: hasta 4 decimales. Montos: hasta 2 decimales. Plazos: meses enteros.</p>
        <div className="cartera-parametros__acciones"><button type="submit" className="cartera-parametros__principal">{guardando ? 'Guardando…' : editor.modo === 'crear' ? 'Crear parámetros' : 'Guardar cambios'}</button><button type="button" onClick={() => { if (!escritura.current) { setEditor(null); setErrores({}); } }}>Cancelar</button></div>
      </fieldset>
    </form>}
  </section>;
}
