import { useEffect, useRef, useState, type FormEvent } from 'react';
import { get } from '../../api/client.ts';
import type { DatosPreparacionWallet, EstadoPreparacionWallet } from './preparacionWallet.ts';

interface Pais { idPais: number; codigo: string; nombre: string; }
interface Departamento { idDepartamento: number; codigoDivipola: string; nombre: string; }
interface Ciudad { idCiudad: number; codigoDivipola: string; nombre: string; tipo: string; }
interface ApiOk<T> { success: boolean; data: T; }

export function PreparacionDatosForm({ estado, ocupado, onGuardar }: {
  estado: EstadoPreparacionWallet;
  ocupado: boolean;
  onGuardar: (datos: DatosPreparacionWallet) => Promise<boolean>;
}) {
  const [direccion, setDireccion] = useState(estado.direccion ?? '');
  const [idDepartamento, setIdDepartamento] = useState(estado.idDepartamento ? String(estado.idDepartamento) : '');
  const [idCiudad, setIdCiudad] = useState(estado.idCiudad ? String(estado.idCiudad) : '');
  const [departamentos, setDepartamentos] = useState<Departamento[]>([]);
  const [ciudades, setCiudades] = useState<Ciudad[]>([]);
  const [cargando, setCargando] = useState(true);
  const [cargandoCiudades, setCargandoCiudades] = useState(false);
  const [error, setError] = useState('');
  const [reintento, setReintento] = useState(0);
  const consultaCiudad = useRef(0);

  useEffect(() => {
    let cancelado = false;
    setCargando(true); setError('');
    void (async () => {
      try {
        const paises = await get<ApiOk<Pais[]>>('/api/catalogos/paises');
        const colombia = paises.success && Array.isArray(paises.data) ? paises.data.filter(p => p.codigo === 'COL') : [];
        if (colombia.length !== 1 || !Number.isSafeInteger(colombia[0].idPais)) throw new Error('Catálogo no disponible.');
        const lista = await get<ApiOk<Departamento[]>>(`/api/catalogos/departamentos?idPais=${colombia[0].idPais}`);
        if (!lista.success || !Array.isArray(lista.data) || !lista.data.length
          || lista.data.some(d => !Number.isSafeInteger(d.idDepartamento) || d.idDepartamento <= 0 || typeof d.nombre !== 'string'))
          throw new Error('Catálogo no disponible.');
        if (!cancelado) setDepartamentos(lista.data);
      } catch {
        if (!cancelado) { setDepartamentos([]); setError('No pudimos cargar las ubicaciones. Actualiza las listas para continuar.'); }
      } finally { if (!cancelado) setCargando(false); }
    })();
    return () => { cancelado = true; };
  }, [reintento]);

  useEffect(() => {
    const consulta = ++consultaCiudad.current;
    setCiudades([]);
    if (!idDepartamento || !departamentos.some(d => String(d.idDepartamento) === idDepartamento)) { setCargandoCiudades(false); return; }
    setCargandoCiudades(true); setError('');
    void get<ApiOk<Ciudad[]>>(`/api/catalogos/ciudades?idDepartamento=${idDepartamento}`).then(r => {
      if (consulta !== consultaCiudad.current) return;
      if (!r.success || !Array.isArray(r.data) || !r.data.length
        || r.data.some(c => !Number.isSafeInteger(c.idCiudad) || c.idCiudad <= 0 || typeof c.nombre !== 'string'))
        throw new Error('Ciudades no disponibles.');
      setCiudades(r.data);
      if (!r.data.some(c => String(c.idCiudad) === idCiudad)) setIdCiudad('');
    }).catch(() => {
      if (consulta === consultaCiudad.current) { setIdCiudad(''); setError('No pudimos cargar las ciudades. Actualiza las listas para continuar.'); }
    }).finally(() => { if (consulta === consultaCiudad.current) setCargandoCiudades(false); });
    return () => { consultaCiudad.current++; };
    // La elección de ciudad no vuelve a pedir el catálogo de su departamento.
  }, [idDepartamento, departamentos]);

  const valido = direccion.trim().length > 0 && direccion.trim().length <= 300
    && departamentos.some(d => String(d.idDepartamento) === idDepartamento)
    && ciudades.some(c => String(c.idCiudad) === idCiudad);

  async function guardar(e: FormEvent) {
    e.preventDefault();
    if (!valido || ocupado || cargando || cargandoCiudades) return;
    await onGuardar({ direccion: direccion.trim(), idDepartamento: Number(idDepartamento), idCiudad: Number(idCiudad) });
  }

  return <form className="onboarding-form" onSubmit={e => void guardar(e)} noValidate>
    <label>Dirección de residencia
      <input autoComplete="street-address" maxLength={300} value={direccion} disabled={ocupado}
        onChange={e => setDireccion(e.target.value)} placeholder="Calle, número y apartamento" />
    </label>
    <label>Departamento
      <select autoComplete="address-level1" value={idDepartamento} disabled={ocupado || cargando}
        onChange={e => { setIdDepartamento(e.target.value); setIdCiudad(''); setCiudades([]); }}>
        <option value="">{cargando ? 'Cargando departamentos...' : 'Selecciona tu departamento'}</option>
        {departamentos.map(d => <option key={d.idDepartamento} value={d.idDepartamento}>{d.nombre}</option>)}
      </select>
    </label>
    <label>Ciudad o municipio
      <select autoComplete="address-level2" value={idCiudad} disabled={ocupado || cargando || cargandoCiudades || !idDepartamento || !ciudades.length}
        onChange={e => setIdCiudad(e.target.value)}>
        <option value="">{cargandoCiudades ? 'Cargando ciudades...' : 'Selecciona tu ciudad o municipio'}</option>
        {ciudades.map(c => <option key={c.idCiudad} value={c.idCiudad}>{c.nombre}</option>)}
      </select>
    </label>
    <small>Ubicaciones del catálogo DANE.</small>
    {error && <div role="alert"><p>{error}</p><button type="button" className="onboarding-link" disabled={ocupado} onClick={() => setReintento(v => v + 1)}>Actualizar listas</button></div>}
    <button type="submit" className="onboarding-primary" disabled={!valido || ocupado || cargando || cargandoCiudades}>{ocupado ? 'Guardando...' : 'Guardar y continuar'}</button>
  </form>;
}
