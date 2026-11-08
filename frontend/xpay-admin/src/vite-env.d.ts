/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string
  // QA | PROD — ver src/config/ambiente.ts
  readonly VITE_XPAY_ENV?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
