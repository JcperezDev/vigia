# Candidato local de Vigía 0.21.0 — SIN FIRMAR · SIN PUBLICAR

Fecha: 2026-10-30 01:25 · rama `debloat-v2` · commit **`9459165`** (el mismo que pasó la suite `s26`). Sustituye al candidato de `c82336b`, que ya no vale: no tenía labHold, ni la retirada de FPS del ayudante, ni las notas corregidas.
**Estado: NOT READY · `SIGNING_BLOCKED`.** Este candidato sirve para revisarlo. No se puede publicar tal cual: le faltan las firmas, que solo se ponen con la clave de producción en la cuenta de firma (ver `SECURITY_KEY_REVIEW.md`, secciones B y C) y eso necesita tu autorización.

## Qué se generó
`VIGIA_RELEASE_SIGN=fuera bash tools/release.sh "…"` (pasos 1 a 4: compila, prueba, arma y hace el manifiesto; **no abre la clave ni toca la red ni git**). rc 0. Carpeta `out/0.21.0/`:

| Archivo | Tamaño | SHA-256 |
|---|---|---|
| `Vigia-0.21.0-win-x64.zip` (343 archivos) | 59 797 719 | `da18b708083d2c1c8b20ced1dc42fdf3cc315bf6b8a95a9e461e9bafe58c991a` |
| `Puente.exe` (2.0.1) = `Vigia-Setup.exe` | 12 554 296 | `ebbcef44e93388cfb33f8a32467a56030bac6d0022cd3fd6d937418dd45d532b` |
| `vigia-manifest.json` | 412 | `845c47a54ecf87c1f55d2425df386be663d857dcbd6a4a2619041291c0bc3e85` |

Comprobado sobre el candidato: selftest del `Vigia.exe` empaquetado **361 OK / 0 FAIL** (datos aislados); `zip-test` (7 rutas peligrosas rechazadas) y `acl-test` (7/7) de Puente; traducciones dentro de `i18n.txt`. El zip no lleva `.pem`, `.key`, `dpapi`, `lab.flag` ni el servicio de sensores.

**Lo que este candidato NO probó:** no se instaló tal cual en la VM. Puente solo acepta manifiestos firmados y este va sin firmar; firmarlo necesita la clave de producción. Las pruebas de instalación, migración, reparación, rollback y UAC se hicieron con paquetes de laboratorio del mismo código: 0.21.8 = `3629a2c` y 0.21.9 = `50ef8fb`. De `50ef8fb` a `9459165` solo cambian textos (notas, aviso, README) y se quitan dos órdenes del ayudante; el selftest y las suites lo cubren. Cuando se firme, hay que repetir en la VM la instalación limpia y la migración desde 0.20.17 con el paquete firmado.

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
- Sin una lectura fiable del procesador, el Monitor dice «Temperatura de CPU no disponible» en vez de mostrar un número. La temperatura solo aparece cuando hay una fuente compatible y una lectura válida, y no todos los procesadores son compatibles. El servicio experimental de sensores no forma parte de esta versión.

> Texto corregido en `b7519e6` (cinco idiomas) para no prometer nada sin comprobar en hardware. Falta tu visto bueno al texto.

## Instalación, migración y recuperación
Procedimiento completo en `docs/INSTALL.md`:
- **Instalación nueva:** «Instalación nueva» (Puente con UAC → `%ProgramFiles%\Vigia`, ACL solo para SYSTEM/Administradores, datos en `%LOCALAPPDATA%\Vigia`). PawnIO: «Sensores y PawnIO».
- **Migración desde 0.20.x:** «Migración desde `%LOCALAPPDATA%\Programs\Vigia`». Se quita la tarea vieja con privilegios máximos, el inicio pasa a Run del usuario y se conservan ajustes, copias e historial.
- **Recuperación:** «Recuperación si algo falla» (`Puente --repair`, `Puente --rollback`, reinstalar desde `Vigia-Setup.exe`). En VM: downgrade rechazado, reparación con DLL dañada, reparación con archivo bloqueado sin dejar nada a medias y rollback 0.21.1 → 0.21.0 con datos intactos (`VM_VALIDATION_REPORT.md`).

## Checklist de seguridad antes de publicar
| # | Comprobación | Estado |
|---|---|---|
| 1 | Builds Vigía / Puente / Puente-test sin errores ni warnings | ✅ `s26` y candidato |
| 2 | Suites: selftest 361/0 · Puente 76/0 · release 31/0 · contrato · i18n 0 errores · Apps 44/0 · Monitor 71/0 · LabGuard 24/0 · guarda 10/0 | ✅ `s26` (`9459165`) |
| 3 | Vigía abre sin UAC; UAC solo al pedir algo administrativo; «No» no eleva; «Sí» levanta solo el ayudante | ✅ VM (administrador) · ✅ VM `estandar`: rc 3, sin escritura, sin procesos (fila 34) · ⛔ aviso con contraseña de admin desde `estandar`: falta credencial autorizada |
| 4 | Ayudante: lista cerrada, rechazos 2–7, se cierra a los 10 min, lo automático no lo renueva ni lo reabre, no corta tareas en marcha | ✅ VM fila 33: tarea de 11 min sin cortes, cierre 600 s tras el fin, re-pedido y «No» limpios |
| 5 | Servicio SYSTEM fuera del paquete público y quitado si estaba | ✅ (`--sensor-service` rc 2; VM filas 29 y 32) |
| 6 | Program Files no modificable por Users / Authenticated Users / Everyone | ✅ VM |
| 7 | LabGuard: órdenes de laboratorio no hacen nada fuera de la VM, también con `VIGIA_LAB=1` y `LAB_ALLOW_GAME=1` | ✅ PC 24/0 · en la VM el ayudante elevado rechazó `labHold` sin flag |
| 8 | Sin material de clave privada en logs ni en el paquete | ✅ (0 coincidencias; 0 archivos de clave en el zip) |
| 9 | Ninguna escritura de firmware; único driver, PawnIO oficial | ✅ auditoría de código |
| 10 | Clave de firma custodiada fuera de esta cuenta, sin acceso de `CodexSandboxUsers` | ⛔ `SIGNING_BLOCKED` (necesita tu autorización) |
| 11 | Temperaturas reales del Ryzen | ⛔ HARDWARE_VALIDATION_PENDING |
| 12 | Revisión externa del servicio SYSTEM | ⛔ EXTERNAL_AUDIT_PENDING (el servicio no va en 0.21.0) |
| 13 | Firmar (cuenta `vigia-firma`) y publicar | ⛔ NEEDS_OWNER_APPROVAL — no ejecutar sin tu orden |
