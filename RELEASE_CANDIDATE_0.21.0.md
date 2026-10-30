# Candidato local de Vigía 0.21.0 — SIN FIRMAR · SIN PUBLICAR

Fecha: 2026-10-30 (noche, 2.ª) · rama `debloat-v2` · commit **`6f8e5ff`** (lo dice `out/0.21.0/candidato.txt`). Respecto al candidato de `a6ffaa5`, el paquete cambia en: Puente solo se sustituye a sí mismo con la publicación entera comprobada (`6ea6126`) y `release.sh` ata lo firmado al commit compilado (`6f8e5ff`). Sustituye a los candidatos de `a6ffaa5` (zip `a393d2fc…a2ee2ad`, Puente `4ed40b0a…4825`) y `c8f0921` (zip `2f90087f…f9974`).
**Estado: NOT READY · `SIGNING_BLOCKED`.** Sirve para revisarlo. No se puede publicar tal cual: le faltan las firmas, que solo se ponen con la clave de producción fuera de este PC, y eso necesita tu autorización (`OWNER_DECISIONS_0.21.0.md` §6).

## Qué se generó
`VIGIA_RELEASE_SIGN=fuera bash tools/release.sh "candidato local"`: pasos 1 a 4 (compila, prueba, arma y hace el manifiesto). **No abre la clave ni toca la red ni git** (no se ejecutó ningún paso de publicación). rc 0. Carpeta `out/0.21.0/`, sin ningún `.sig`:

| Archivo | Tamaño | SHA-256 |
|---|---|---|
| `Vigia-0.21.0-win-x64.zip` (343 entradas) | 59 803 716 | `6254ed4770768da13364db04441396b24d9a5a5cdccda0430c282c31f3c4836c` |
| `Puente.exe` (2.0.1) = `Vigia-Setup.exe` | 12 557 783 | `cb68d8b779c98cb40b7070400e0b02bc92b215c3b0d164db862869d47dcea257` |
| `vigia-manifest.json` (versión 0.21.0, Puente 2.0.1, tamaños y SHA-256 de los dos de arriba) | 412 | `dd165736f860c6b36fd3380454c9e866c23c91ed3917294a3569b35721d5e1e5` |

Hashes anteriores (`a6ffaa5`: zip `a393d2fc…`; `c8f0921`: zip `2f90087f…f9974`; `d8e24d9`: zip `6e2ba33d…28d3`) quedan sustituidos. **Versión de Puente:** la última publicada es la de `v0.20.17`, **Puente 1.4.0**; 2.0.1 no se publicó nunca, así que el 2.0.1 de este candidato es mayor que el instalado en cualquier PC real y lo sustituye de verdad (comprobado por versión y SHA‑256 en la VM). Cambiar sus bytes antes de publicar no deja a nadie con un 2.0.1 distinto. `release.sh` lo comprueba contra la última etiqueta.

**De qué código salió:** `candidato.txt` guarda el commit (`6f8e5ff19aad…`) y las cuatro huellas. `VIGIA_RELEASE_SIGNED=1` se niega a publicar si `app/` cambió desde ese commit o si lo que vuelve firmado no es lo compilado (release-test 33/0, casos nuevos). `node tools/candidate-check.js out/0.21.0`: **19 OK / 0 FAIL** (origen y huellas, manifiesto contra zip y Puente, Puente 2.0.1 > 1.4.0, compilación pública, `--install-check`, `--sensor-service` desconocido, `LocationGuard.Harden` presente, sin `WlanGetNetworkBssList`, misma clave pública en Vigía y Puente, `NextKeys` vacía). Sin `.sig`.

Comprobado sobre el candidato:
- selftest del `Vigia.exe` empaquetado (paso 2 de `release.sh`, sobre `6f8e5ff`): **371 OK / 0 FAIL**, con datos aislados.
- Las comprobaciones de abajo son del candidato de `a6ffaa5`. El código de Vigía no cambió desde entonces (solo `app/Puente` y `tools/`); `candidate-check.js` repitió sobre este las de versión, `--sensor-service`, clave y ubicación.
- Puente: `zip-test` (7/7: rechaza `..`, rutas absolutas, barra inicial, flujos NTFS) y `acl-test` (todo bien).
- `Vigia.exe` del zip: versión 0.21.0.0, manifiesto `asInvoker`, ninguna cadena `requireAdministrator`; ningún archivo de PawnIO ni `.sys`.
- `changelog.json` del zip lleva la nota de los seis ajustes de privacidad y la del arreglo de ubicación; `i18n-check`: en/pt/fr/de 2435 claves, 0 errores.
- `preparar-relevo comprobar`: Puente y Vigía confían en la misma clave y `NextKeys` está vacía.
- `--sensor-service`: rc 2.
- El zip no lleva `.pem`, `.key`, `dpapi`, `lab.flag`, `vm-cred`, `.sys` ni el servicio de sensores (0 coincidencias). El log del candidato y `out/0.21.0` no contienen material de clave privada (0).

**Relevo desde 0.20.17** (Puente 1.4.0 → 2.0.1/2.0.2 → 2.0.3, SHA-256 comprobado, mismo número no sustituye, firmas inválidas y downgrade con firma válida rechazados, datos conservados): fila 40, paquetes de laboratorio desde `fea4ae0`.

**Instalación, migración, Puente y rollback** se probaron en VigiaLab el 30-10 con paquetes de laboratorio firmados con la clave de laboratorio: filas 35–37 (`7202134`) y 38 (relevo de claves A → B, instalación limpia, puente, rechazo de la firma vieja, rollback y reparación, `78f3fdf`) de `VM_VALIDATION_REPORT.md`. Desde `7202134` el código de la app solo cambió en las notas (`aa0bb59`), así que esas pruebas siguen valiendo para este candidato. **Lo que falta:** instalar *este* candidato una vez firmado con la clave de producción (Puente rechaza manifiestos sin firmar). Cuando se firme, hay que repetir en la VM la instalación limpia y la migración desde 0.20.17 con el paquete firmado (unos 10 minutos).

Las firmas de laboratorio no se mezclan con este candidato: el Puente con la clave real rechaza los paquetes de laboratorio (comprobado al armar 0.21.0 y 0.21.1 de laboratorio).

## Coherencia de versiones
- Publicada: **0.20.17** (lo que hay en `vigia` y `vigia-releases`). Nunca se publicó nada 0.21.x.
- **0.21.0 sigue siendo válida como siguiente versión**: es mayor que 0.20.17, Vigía rechaza manifiestos más viejos que la versión instalada y Puente no se baja de versión. 0.21.1–0.21.8 (y los paquetes de relevo 0.21.1–0.21.3 de la fila 38) solo existen en VigiaLab, firmados con claves de laboratorio; ninguna instalación real los vio ni los puede aceptar, y Vigía compara contra el manifiesto publicado, no contra lo que hubo en la VM, así que no afectan a las actualizaciones.
- Las instalaciones 0.20.x buscan actualizaciones en `vigia-releases`: `release.sh` sigue publicando también ahí (`MIRROR_REPO`), así que 0.20.17 puede llegar a 0.21.0. Migración 0.20.17 → 0.21.0 probada en VM el 30-10 sobre `7202134` (fila 35; datos con el mismo hash).
- Puente 2.0.1 confía en la misma clave pública que 0.20.17 (`PublicKey`) y `NextKeys` está vacía: publicar 0.21.0 no corta la actualización ni la recuperación de las instalaciones existentes. Si se rota la clave, el orden seguro está en `docs/SIGNING.md` (primero una versión firmada con la clave actual que ya confíe en la nueva). `preparar-relevo.js` sube además la versión de Puente (`78f3fdf`): sin eso, una instalación con Puente de la misma versión no lo cambiaba y se quedaba con la lista vieja. Probado de punta a punta en VigiaLab (fila 38): saltarse la versión puente o firmar con la clave vieja tras rotar no instala nada.

## Notas de versión (las que verá la gente; también en en/pt/fr/de en `changelog.json`)
**Más segura por dentro**
- Vigía ahora vive en Archivos de programa y abre como tu usuario, sin pedir permisos de administrador solo por abrir.
- Cuando algo necesita permisos de administrador, Vigía te los pide en ese momento y solo para eso.
- Arrancar con Windows ya no usa una tarea con permisos de administrador.
- Las copias se revisan enteras antes de restaurarlas: una copia cambiada a mano no puede tocar nada fuera de lo que guarda Vigía.
- Puente pasa tu instalación de antes a la nueva sin perder tus ajustes, copias ni historial, y nunca instala una versión más vieja.
- Apps: inventario completo con vista previa, historial y forma de recuperar lo que se quitó cuando Windows lo permite.
- El ayudante con permisos de administrador se cierra solo tras 10 minutos sin que pidas nada; las lecturas automáticas del Monitor no lo mantienen abierto.
- Privacidad: seis ajustes nuevos, opcionales, documentados por Microsoft. Cada uno te enseña lo que cambia antes de aplicarlo y ninguno entra en «Aplicar todo» ni en el modo simple.
- Sin una lectura fiable del procesador, el Monitor dice «Temperatura de CPU no disponible» en vez de mostrar un número. La temperatura solo aparece cuando hay una fuente compatible y una lectura válida, y no todos los procesadores son compatibles. El servicio experimental de sensores no forma parte de esta versión.

> Texto corregido en `b7519e6` (temperatura) y `aa0bb59` (privacidad), cinco idiomas. Contrastado con el código: los seis ajustes de privacidad no entran en «Aplicar todo» ni en el modo simple, el ayudante se cierra a los 10 min (VM fila 33), el texto de temperatura sale sin valor (fila 36) y `--sensor-service` no existe en la compilación pública (rc 2). Lo que el texto no puede prometer: temperaturas reales en tu Ryzen (sin validar en hardware).

## Instalación, migración y recuperación
Procedimiento completo en `docs/INSTALL.md`:
- **Instalación nueva:** «Instalación nueva» (Puente con UAC → `%ProgramFiles%\Vigia`, ACL solo para SYSTEM/Administradores, datos en `%LOCALAPPDATA%\Vigia`). PawnIO: «Sensores y PawnIO».
- **Migración desde 0.20.x:** «Migración desde `%LOCALAPPDATA%\Programs\Vigia`». Se quita la tarea vieja con privilegios máximos, el inicio pasa a Run del usuario y se conservan ajustes, copias e historial.
- **Recuperación:** «Recuperación si algo falla» (`Puente --repair`, `Puente --rollback`, reinstalar desde `Vigia-Setup.exe`). En VM: downgrade rechazado, reparación con DLL dañada, reparación con archivo bloqueado sin dejar nada a medias y rollback 0.21.1 → 0.21.0 con datos intactos (`VM_VALIDATION_REPORT.md`).

## Checklist de seguridad antes de publicar
| # | Comprobación | Estado |
|---|---|---|
| 1 | Builds Vigía / Puente / Puente-test sin errores ni warnings | ✅ `s26`; Puente-test y candidato sobre `6f8e5ff` |
| 2 | Suites: selftest 362/0 · Puente 76/0 · release 31/0 · contrato · i18n 0 errores · Apps 44/0 · Monitor 71/0 · LabGuard 24/0 · guarda 10/0 · firma 39/0 · `comprobar` | ✅ `s27` completa sobre `aa0bb59` (mismo código de app que el HEAD) + firma 39/0 (claves de prueba) y `comprobar` rc 0 |
| 3 | Vigía abre sin UAC; UAC solo al pedir algo administrativo; «No» no eleva; «Sí» levanta solo el ayudante | ✅ VM (administrador) · ✅ VM `estandar`: rc 3, sin escritura, sin procesos (fila 34) · ⛔ aviso con contraseña de admin desde `estandar`: falta credencial autorizada |
| 4 | Ayudante: lista cerrada, rechazos 2–7, se cierra a los 10 min, lo automático no lo renueva ni lo reabre, no corta tareas en marcha | ✅ VM fila 33: tarea de 11 min sin cortes, cierre 600 s tras el fin, re-pedido y «No» limpios |
| 5 | Servicio SYSTEM fuera del paquete público y quitado si estaba | ✅ (`--sensor-service` rc 2; VM filas 29 y 32) |
| 6 | Program Files no modificable por Users / Authenticated Users / Everyone | ✅ VM |
| 7 | LabGuard: órdenes de laboratorio no hacen nada fuera de la VM, también con `VIGIA_LAB=1` y `LAB_ALLOW_GAME=1` | ✅ PC 24/0 · en la VM el ayudante elevado rechazó `labHold` sin flag |
| 8 | Sin material de clave privada en logs ni en el paquete | ✅ (0 coincidencias; 0 archivos de clave en el zip) |
| 9 | Ninguna escritura de firmware; único driver, PawnIO oficial | ✅ auditoría de código |
| 10 | Clave de firma custodiada fuera de esta cuenta, sin acceso de `CodexSandboxUsers` | 🟡 `CodexSandboxUsers` ya sin acceso a `.vigia` (30-10, `--key-check` rc 0) · ⛔ la clave sigue sin cifrar en la cuenta de desarrollo con UAC desactivado y pudo estar expuesta antes: `SIGNING_BLOCKED` |
| 10b | Relevo de claves sin cortar actualizaciones | ✅ probado con claves de laboratorio en VigiaLab (fila 38); con la clave real: NEEDS_OWNER_APPROVAL |
| 11 | Temperaturas reales del Ryzen | ⛔ HARDWARE_VALIDATION_PENDING |
| 12 | Revisión externa del servicio SYSTEM | ⛔ EXTERNAL_AUDIT_PENDING (el servicio no va en 0.21.0) |
| 13 | Firmar en un equipo aparte (`firmar-fuera.ps1`), repetir instalación limpia y migración en VigiaLab con ese paquete exacto, publicar | ⛔ NEEDS_OWNER_APPROVAL — no ejecutar sin tu orden |
| 14 | Puente solo se sustituye con la publicación entera comprobada (zip malo o Puente que no acepta su propia firma ⇒ no cambia nada, ni las claves de confianza) | ✅ PC `puente-test` 85/0 (`6ea6126`) · fallo reproducido en VM con el código anterior (fila 41) · ⏳ VM con el arreglo pendiente (pausada por juego abierto) |
| 15 | Lo firmado fuera = lo compilado y probado, del mismo commit | ✅ `candidato.txt` + `release.sh` (release-test 33/0) · `candidate-check.js` 19/0 |
| 16 | Wi‑Fi conectado con ubicación apagada: sin aviso, señal correcta, sin datos de red en logs | ⛔ HARDWARE_VALIDATION_PENDING (aquí no hay Wi‑Fi conectado; pasos en `LOCATION_PRIVACY_FIX.md`) |
