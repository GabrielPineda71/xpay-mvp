import { useEffect, useState, type FormEvent } from 'react';
import { useMyBrebKey, type BrebKeyType } from '../../hooks/useMyBrebKey.ts';
import { fmtDate } from '../../utils.ts';

const KEY_TYPES: { value: BrebKeyType; label: string }[] = [
  { value: 'ID',    label: 'Cédula / ID' },
  { value: 'PHONE', label: 'Número de celular' },
  { value: 'EMAIL', label: 'Correo electrónico' },
  { value: 'ALPHA', label: 'Alias alfanumérico' },
  { value: 'BCODE', label: 'Código Bre-B' },
];

// XPAY-392 (Fase 1 de XPAY-391) — sección "Mi llave Bre-B" de Perfil.
//
// Gestiona ÚNICAMENTE la asociación LOCAL de la llave propia:
//   - GET  /api/breb/mi-llave  (cargar estado actual)
//   - POST /api/breb/mi-llave  (registrar/actualizar)
//
// NO ejecuta, ni aquí ni al montar este componente, ninguna de estas
// operaciones (deliberadamente prohibidas por diseño XPAY-391/392):
//   - POST /api/breb/mi-llave/resolver (Resolve real contra Passport);
//   - CreateKey / DeleteKey Passport;
//   - nada relacionado con el retiro financiero.
// El Resolve inmediatamente previo a un retiro sigue viviendo,
// exclusivamente, en UserWalletPage.tsx (tab "retirar-breb").
//
// Todos los campos mostrados (keyValueMasked, estado) ya vienen
// enmascarados/sanitizados por el backend — este componente nunca
// introduce ni muestra un valor crudo nuevo.
export function BrebMyKeySection() {
  const { llave, loading, registrando, mensaje, cargar, registrar } = useMyBrebKey();
  const [keyType, setKeyType] = useState<BrebKeyType>('ID');
  const [keyValue, setKeyValue] = useState('');

  useEffect(() => { void cargar(); }, [cargar]);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const ok = await registrar(keyType, keyValue);
    if (ok) setKeyValue('');
  }

  return (
    <section className="breb-profile-section" aria-label="Mi llave Bre-B">
      <h3 className="profile-section-title">Mi llave Bre-B</h3>

      {loading ? (
        <div className="loading">Cargando llave Bre-B...</div>
      ) : (
        <>
          <div className="breb-profile-status-card">
            <span className="breb-profile-status-label">Estado:</span>
            {llave ? (
              <>
                <span className={`breb-badge breb-badge-${llave.estado.toLowerCase().replace(/_/g, '-')}`}>
                  {llave.estado.replace(/_/g, ' ')}
                </span>
                <span className="breb-key-masked">{llave.keyType} · {llave.keyValueMasked}</span>
              </>
            ) : (
              <span className="breb-badge breb-badge-no-registrada">NO REGISTRADA</span>
            )}
          </div>
          {llave?.fechaValidacion && (
            <div className="breb-profile-meta">Validada: {fmtDate(llave.fechaValidacion)}</div>
          )}

          <h4 className="profile-section-subtitle">
            {llave ? 'Actualizar llave Bre-B' : 'Registrar llave Bre-B'}
          </h4>
          <form className="breb-profile-form" onSubmit={e => void handleSubmit(e)}>
            <label>
              Tipo de llave
              <select value={keyType} onChange={e => setKeyType(e.target.value as BrebKeyType)}>
                {KEY_TYPES.map(t => <option key={t.value} value={t.value}>{t.label}</option>)}
              </select>
            </label>
            <label>
              Valor de la llave
              <input
                type="text"
                value={keyValue}
                onChange={e => setKeyValue(e.target.value)}
                placeholder={
                  keyType === 'ID'    ? 'Ej: 1234567890' :
                  keyType === 'PHONE' ? 'Ej: 3001234567' :
                  keyType === 'EMAIL' ? 'Ej: correo@banco.com' :
                  keyType === 'ALPHA' ? 'Ej: mi-alias-breb' : 'Ej: BREB-XXXXXX'
                }
                autoComplete="off"
              />
            </label>
            <p className="breb-profile-note">
              Al registrar confirmas que esta llave Bre-B te pertenece y corresponde a tu cuenta bancaria.
              No se puede retirar a llaves de terceros.
            </p>
            <button type="submit" className="btn-breb" disabled={registrando || !keyValue.trim()}>
              {registrando ? 'Registrando...' : llave ? 'Actualizar llave' : 'Registrar llave'}
            </button>
            {mensaje && (
              <span className={mensaje.ok ? 'breb-msg-ok' : 'breb-msg-err'}>{mensaje.text}</span>
            )}
          </form>
        </>
      )}
    </section>
  );
}
