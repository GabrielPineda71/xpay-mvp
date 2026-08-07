interface SedeOption { idEstablecimiento: number; nombreEstablecimiento: string }

interface CajaFiltersProps {
  mostrarFiltroSede: boolean;
  sedes:             SedeOption[];
  idEstablecimiento: number | '';
  estado:            string;
  desde:             string;
  hasta:             string;
  onChange: (next: { idEstablecimiento?: number | ''; estado?: string; desde?: string; hasta?: string }) => void;
}

const ESTADOS = ['', 'ABIERTA', 'EN_CUADRE', 'CERRADA', 'CON_DIFERENCIA', 'CERRADA_AUTOMATICAMENTE', 'REVISADA'];

export function CajaFilters({ mostrarFiltroSede, sedes, idEstablecimiento, estado, desde, hasta, onChange }: CajaFiltersProps) {
  return (
    <div className="caja-filters">
      {mostrarFiltroSede && (
        <div className="caja-filter-field">
          <label htmlFor="caja-filtro-sede">Sede</label>
          <select
            id="caja-filtro-sede"
            value={idEstablecimiento}
            onChange={e => onChange({ idEstablecimiento: e.target.value ? Number(e.target.value) : '' })}
          >
            <option value="">Todas</option>
            {sedes.map(s => (
              <option key={s.idEstablecimiento} value={s.idEstablecimiento}>{s.nombreEstablecimiento}</option>
            ))}
          </select>
        </div>
      )}
      <div className="caja-filter-field">
        <label htmlFor="caja-filtro-estado">Estado</label>
        <select id="caja-filtro-estado" value={estado} onChange={e => onChange({ estado: e.target.value })}>
          {ESTADOS.map(e => <option key={e} value={e}>{e || 'Todos'}</option>)}
        </select>
      </div>
      <div className="caja-filter-field">
        <label htmlFor="caja-filtro-desde">Desde</label>
        <input id="caja-filtro-desde" type="date" value={desde} onChange={e => onChange({ desde: e.target.value })} />
      </div>
      <div className="caja-filter-field">
        <label htmlFor="caja-filtro-hasta">Hasta</label>
        <input id="caja-filtro-hasta" type="date" value={hasta} onChange={e => onChange({ hasta: e.target.value })} />
      </div>
    </div>
  );
}
