import { useNavigate } from 'react-router-dom';
import { BrebMyKeySection } from '../components/profile/BrebMyKeySection.tsx';
import { MiInformacionSection } from '../components/profile/MiInformacionSection.tsx';
import { CambiarContrasenaSection } from '../components/profile/CambiarContrasenaSection.tsx';

// XPAY-401 (Perfil Fase 2C) — página real de Perfil (ruta mi-wallet/perfil).
// Misma página de siempre (XPAY-392), ahora con 3 secciones completas:
//   A. Mi información  -> MiInformacionSection (GET/PATCH /api/usuarios/mi-perfil, XPAY-399)
//   B. Seguridad        -> CambiarContrasenaSection (POST /api/auth/cambiar-clave, XPAY-400)
//   C. Mi llave Bre-B   -> BrebMyKeySection (XPAY-392, SIN CAMBIOS — GET/POST
//                          /api/breb/mi-llave; nunca llama a /resolver, eso
//                          sigue exclusivamente en Retirar)
//
// NO implementa todavía (fuera de alcance de XPAY-401): foto/avatar real
// (upload/Blob/endpoint), auditoría visible en UI.
export function ProfilePage() {
  const navigate = useNavigate();

  return (
    <div className="page profile-page">
      <button
        type="button"
        className="profile-back-link"
        onClick={() => navigate('/mi-wallet')}
      >
        ← Mi Wallet
      </button>

      <div className="profile-header">
        {/* XPAY-401 PASO 12 — avatar genérico/iniciales, presentación local
            pura, sin backend: mismo ícono SVG que ya existía en XPAY-392, sin
            upload, sin selector de archivo, sin nueva columna/endpoint. */}
        <div className="profile-header-avatar" aria-hidden="true">
          <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
            <circle cx="12" cy="8.5" r="3.5" />
            <path d="M4.5 20c1-3.8 4-5.8 7.5-5.8s6.5 2 7.5 5.8" />
          </svg>
        </div>
        <h2 className="profile-header-title">Perfil</h2>
      </div>

      {/* A. Mi información */}
      <MiInformacionSection />

      {/* B. Seguridad */}
      <CambiarContrasenaSection />

      {/* C. Mi llave Bre-B — componente de XPAY-392, sin ninguna
          modificación de comportamiento (solo se conserva su posición ya
          aprobada dentro de esta misma página). */}
      <BrebMyKeySection />
    </div>
  );
}
