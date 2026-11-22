import { useState, type FormEvent } from 'react';
import type { LlavePreparacionWallet } from './preparacionWallet.ts';

const TIPOS = [ ['ID', 'Documento de identidad'], ['PHONE', 'Número de celular'], ['EMAIL', 'Correo electrónico'], ['ALPHA', 'Alias alfanumérico'], ['BCODE', 'Código Bre-B'] ];

export function PreparacionLlaveForm({ ocupado, onGuardar }: { ocupado: boolean; onGuardar: (llave: LlavePreparacionWallet) => Promise<boolean> }) {
  const [tipo, setTipo] = useState('ID');
  const [valor, setValor] = useState('');
  const [propia, setPropia] = useState(false);
  async function guardar(e: FormEvent) {
    e.preventDefault();
    if (ocupado || !propia || !valor.trim()) return;
    if (await onGuardar({ omitir: false, tipoLlave: tipo, valorLlave: valor.trim(), confirmaPropiedad: true })) { setValor(''); setPropia(false); }
  }
  return <>
    <p>Úsala para retirar dinero a tu propia cuenta bancaria. Puedes agregarla después.</p>
    <form className="onboarding-form" onSubmit={e => void guardar(e)}>
      <label>Tipo de llave
        <select value={tipo} disabled={ocupado} onChange={e => { setTipo(e.target.value); setValor(''); setPropia(false); }}>
          {TIPOS.map(([id, texto]) => <option key={id} value={id}>{texto}</option>)}
        </select>
      </label>
      <label>Mi llave Bre-B
        <input value={valor} disabled={ocupado} autoComplete="off" maxLength={254} onChange={e => setValor(e.target.value)}
          placeholder={tipo === 'ALPHA' ? 'Ejemplo: @gpo118' : tipo === 'PHONE' ? 'Ejemplo: 3001234567' : tipo === 'EMAIL' ? 'Ejemplo: correo@banco.com' : 'Escribe tu llave'} />
      </label>
      <label className="onboarding-check"><input type="checkbox" checked={propia} disabled={ocupado} onChange={e => setPropia(e.target.checked)} />
        <span>Confirmo que esta llave me pertenece y corresponde a mi propia cuenta bancaria.</span>
      </label>
      <small>La llave quedará pendiente de validación. Debes verificarla antes de retirar al banco.</small>
      <button type="submit" className="onboarding-primary" disabled={ocupado || !propia || !valor.trim()}>{ocupado ? 'Guardando...' : 'Guardar y continuar'}</button>
    </form>
    <button type="button" className="onboarding-secondary" disabled={ocupado} onClick={() => void onGuardar({ omitir: true, confirmaPropiedad: false })}>Registrar después</button>
    <small>La llave no es necesaria para enviar entre Wallets ni para pagar con QR.</small>
  </>;
}
