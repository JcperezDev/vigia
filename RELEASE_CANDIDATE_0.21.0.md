# Candidato local de Vigía 0.21.0 — SIN FIRMAR · SIN PUBLICAR

Fecha: 2026-10-30 · rama `debloat-v2` · commit `c82336b` (código igual a `c260497`, que pasó la suite `s25`).
**Estado: NOT READY · `SIGNING_BLOCKED`.** Este candidato sirve para revisarlo. No se puede publicar tal cual: le faltan las firmas, que solo se ponen con la clave de producción en la cuenta de firma (ver `SECURITY_KEY_REVIEW.md`, secciones B y C) y eso necesita tu autorización.

## Qué se generó
`VIGIA_RELEASE_SIGN=fuera bash tools/release.sh "…"` (pasos 1 a 4: compila, prueba, arma y hace el manifiesto; **no abre la clave ni toca la red ni git**). rc 0. Carpeta `out/0.21.0/`:

| Archivo | Tamaño | SHA-256 |
|---|---|---|
| `Vigia-0.21.0-win-x64.zip` (343 archivos) | 59 789 222 | `9fb4dd4209fdd37c136a2f3e83f3fba168beda0c5bebc0c810e0edb93c7f667f` |
| `Puente.exe` (2.0.1) = `Vigia-Setup.exe` | 12 554 293 | `3532a1695ba6f563d66afafca6e69ed9cbae18a9a2c690261078157a971bbba2` |
| `vigia-manifest.json` | 412 | `8ec0b2752dc7f72e8b81712fffafc7060bdb3e227252349028d267c5d2bd155a` |

Comprobado sobre el candidato: selftest del `Vigia.exe` empaquetado **358 OK / 0 FAIL** (con datos aislados); `zip-test` y `acl-test` de Puente y traducciones dentro de `release.sh`; ningún `.pem`, `.key`, `.dpapi` ni `lab.flag` en el zip; el manifiesto dice `0.21.0` y Puente `2.0.1`. El servicio SYSTEM no va dentro (`release.sh` compila con `-p:SensorService=false` y para si `--sensor-service` no devuelve 2).

Las firmas de laboratorio no se mezclan con este candidato: los paquetes 0.21.1–0.21.8 de VigiaLab se firmaron con una clave ficticia que el Puente público no acepta.

## Coherencia de versiones
- Publicada: **0.20.17**. Nunca se publicó nada 0.21.x.
- **0.21.0 sigue siendo válida como siguiente versión**: es mayor que 0.20.17, Vigía rechaza manifiestos más viejos que la versión instalada y Puente no se baja de versión. 0.21.1–0.21.8 solo existen en VigiaLab, con clave de laboratorio; ninguna instalación real las vio ni las puede aceptar.
- Las instalaciones 0.20.x buscan actualizaciones en `vigia-releases`: `release.sh` sigue publicando también ahí (`MIRROR_REPO`), así que 0.20.17 puede llegar a 0.21.0. Migración 0.20.17 → 0.21.0 probada en VM (datos con el mismo hash).
- Puente 2.0.1 confía en la misma clave pública que 0.20.17 (`PublicKey`) y `NextKeys` está vacía: publicar 0.21.0 no corta la actualización ni la recuperación de las instalaciones existentes. Si se rota la clave, el orden seguro está en `docs/SIGNING.md` (primero una versión firmada con la clave actual que ya confíe en la nueva).

## Notas de versión (las que verá la gente; también en en/pt/fr/de en `changelog.json`)
**Más segura por dentro**
- Vigía ahora vive en Archivos de programa y abre como tu usuario, sin pedir permisos de administrador solo por abrir.
- Cuando algo necesita permisos de administrador, Vigía te los pide en ese momento y solo para eso.
- Arrancar con Windows ya no usa una tarea con permisos de administrador.
- Las copias se revisan enteras antes de restaurarlas: una copia cambiada a mano no puede tocar nada fuera de lo que guarda Vigía.
- Puente pasa tu instalación de antes a la nueva sin perder tus ajustes, copias ni historial, y nunca instala una versión más vieja.
- Apps: inventario completo con vista previa, historial y forma de recuperar lo que se quitó cuando Windows lo permite.
- El ayudante con permisos de administrador se cierra solo tras 10 minutos sin que pidas nada; las lecturas automáticas del Monitor no lo mantienen abierto.
- Sin una lectura fiable del procesador, el Monitor dice «Temperatura de CPU no disponible» y el motivo, en vez de mostrar un número. Como Vigía ya no abre con permisos de administrador, los grados del procesador se ven mientras el ayudante está abierto.

> ⚠️ La segunda frase del último punto describe lo que hace el código, pero **no está comprobada en un PC físico** (la VM no tiene sensores). Antes de publicar: validarla o quitarla (en los cinco idiomas).

## Instalación, migración y recuperación
Procedimiento completo en `docs/INSTALL.md`:
- **Instalación nueva:** «Instalación nueva» (Puente con UAC → `%ProgramFiles%\Vigia`, ACL solo para SYSTEM/Administradores, datos en `%LOCALAPPDATA%\Vigia`). PawnIO: «Sensores y PawnIO».
- **Migración desde 0.20.x:** «Migración desde `%LOCALAPPDATA%\Programs\Vigia`». Se quita la tarea vieja con privilegios máximos, el inicio pasa a Run del usuario y se conservan ajustes, copias e historial.
- **Recuperación:** «Recuperación si algo falla» (`Puente --repair`, `Puente --rollback`, reinstalar desde `Vigia-Setup.exe`). En VM: downgrade rechazado, reparación con DLL dañada, reparación con archivo bloqueado sin dejar nada a medias y rollback 0.21.1 → 0.21.0 con datos intactos (`VM_VALIDATION_REPORT.md`).

## Checklist de seguridad antes de publicar
| # | Comprobación | Estado |
|---|---|---|
| 1 | Builds Vigía / Puente / Puente-test sin errores ni warnings | ✅ `s25` y candidato |
| 2 | Suites: selftest 358/0 · Puente 76/0 · release 31/0 · contrato · i18n 0 errores · Apps 44/0 · Monitor 71/0 · LabGuard 22/0 · guarda 10/0 | ✅ `s25` |
| 3 | Vigía abre sin UAC; UAC solo al pedir algo administrativo; «No» no eleva; «Sí» levanta solo el ayudante | ✅ VM (administrador) · 🟡 usuario estándar en PC físico |
| 4 | Ayudante: lista cerrada, rechazos 2–7, se cierra a los 10 min, lo automático no lo renueva ni lo reabre, no corta tareas en marcha | ✅ VM y selftest (tarea larga: solo simulada) |
| 5 | Servicio SYSTEM fuera del paquete público y quitado si estaba | ✅ (`--sensor-service` rc 2; VM filas 29 y 32) |
| 6 | Program Files no modificable por Users / Authenticated Users / Everyone | ✅ VM |
| 7 | LabGuard: órdenes de laboratorio no hacen nada fuera de la VM, también con `VIGIA_LAB=1` y `LAB_ALLOW_GAME=1` | ✅ PC 22/0 |
| 8 | Sin material de clave privada en logs ni en el paquete | ✅ (0 coincidencias; 0 archivos de clave en el zip) |
| 9 | Ninguna escritura de firmware; único driver, PawnIO oficial | ✅ auditoría de código |
| 10 | Clave de firma custodiada fuera de esta cuenta, sin acceso de `CodexSandboxUsers` | ⛔ `SIGNING_BLOCKED` (necesita tu autorización) |
| 11 | Temperaturas reales del Ryzen | ⛔ HARDWARE_VALIDATION_PENDING |
| 12 | Revisión externa del servicio SYSTEM | ⛔ EXTERNAL_AUDIT_PENDING (el servicio no va en 0.21.0) |
| 13 | Firmar (cuenta `vigia-firma`) y publicar | ⛔ NEEDS_OWNER_APPROVAL — no ejecutar sin tu orden |
