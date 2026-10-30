# Instalar, migrar y recuperar Vigía

Desde 0.21.0 Vigía se instala en `%ProgramFiles%\Vigia` con **Puente 2.0** (`app/Puente`). Las rutas salen siempre de
`Environment.SpecialFolder` (nunca `C:\Program Files` escrito a mano): da igual el idioma de Windows o la letra de la unidad.

## Dónde queda cada cosa

| Ruta | Qué hay | Quién escribe |
|---|---|---|
| `%ProgramFiles%\Vigia\Puente.exe` | instalador/actualizador | SYSTEM, Administradores |
| `%ProgramFiles%\Vigia\versiones\<x.y.z>\` | cada versión en su carpeta, con `.vigia-archivos.json` (tamaño y SHA-256 de cada archivo) | SYSTEM, Administradores |
| `%ProgramFiles%\Vigia\estado.json` | versión activa, anterior, la más nueva vista y el piso de seguridad | SYSTEM, Administradores |
| `%ProgramFiles%\Vigia\diario.json` | en qué paso va una instalación (para seguir o deshacer tras un corte) | SYSTEM, Administradores |
| `%ProgramFiles%\Vigia\.descargas\` | lo descargado, antes y durante la comprobación de firma | SYSTEM, Administradores |
| `%ProgramData%\Vigia\registros\puente.log` | registro de Puente | SYSTEM, Administradores |
| `%ProgramData%\Vigia\claves\copias.key` | clave del sello de las copias (la crea el ayudante la primera vez que la necesita) | solo SYSTEM y Administradores (ni lectura para usuarios) |
| `%LOCALAPPDATA%\Vigia\` | ajustes, historial, copias, logs, estado de la ventana | tu usuario |
| `Documentos\Vigía\` | exportaciones (JSON/CSV de Apps) | tu usuario |

### Permisos NTFS de `%ProgramFiles%\Vigia`

Puente los pone en cada instalación (`Acl.Protect` y `Acl.ResetChildren` en `app/Puente/Layout.cs`):

- Herencia **cortada** (`SetAccessRuleProtection(true, false)`): no se hereda nada de `Program Files` ni de lo que hubiera.
- SYSTEM: control total. Administradores: control total. **Users: leer y ejecutar.** ALL APPLICATION PACKAGES: leer y ejecutar
  (lo necesita WebView2).
- Dueño de cada archivo y carpeta: **Administradores** (Puente fija el dueño por defecto de su token con `UseAdminsAsOwner`,
  para que nadie herede la propiedad y con ella el derecho a cambiar permisos).
- Nada se oculta ni se marca de solo lectura: la protección es la ACL.
- Junctions y enlaces simbólicos: si la raíz, `versiones`, una versión o cualquier archivo dentro es un punto de reparse,
  Puente **no instala** y `--acl-test` lo marca como problema. Lo mismo para `estado.json` y el diario al leerlos.
- Antes de dar por buena una versión, `Acl.Problem` revisa la carpeta entera: falla si Users, Authenticated Users, Everyone,
  INTERACTIVE o paquetes de apps tienen cualquier derecho de escritura, atributos, borrado, cambio de permisos o de propiedad,
  si el dueño no es Administradores/SYSTEM/TrustedInstaller, o si hay enlaces.
- `Puente.exe --acl-test` es la autoprueba de esas reglas (en una carpeta de `%TEMP%`: detecta «Usuarios: modificar», enlaces
  dentro, y se niega a instalar en una carpeta que es un enlace).

## Instalación nueva

1. El usuario abre `Vigia-Setup.exe` (es Puente). Puente abre sin permisos y pide UAC solo al pulsar «Instalar».
2. Descarga a `.descargas\` (carpeta protegida), con reanudación por Range.
3. Comprueba la firma ECDSA P-256 del manifiesto (`vigia-manifest.json` + `.sig`), y que el manifiesto dice esa versión,
   ese tamaño y ese SHA-256 para el paquete y para Puente. Rechaza versiones por debajo de la más nueva vista o del piso.
4. Comprueba espacio libre (≈5 veces el paquete).
5. Extrae a `versiones\<x.y.z>.preparando\` (sin `..`, sin rutas absolutas, sin enlaces), pone permisos y comprueba
   **cada archivo** contra el manifiesto firmado. Así lo que se ejecuta es lo comprobado: entre la comprobación y el uso
   todo está en una carpeta en la que solo escriben administradores (cierra el TOCTOU).
6. Abre la versión nueva con `Vigia.exe --install-check` **como tu usuario** (sin permisos); si no responde 0 en 60 s, no
   se activa.
7. Cambia `estado.json` en una sola escritura (archivo temporal + reemplazo). Ahí la versión nueva pasa a ser la activa.
8. Accesos directos en el menú Inicio común; entrada de «Aplicaciones instaladas» en HKLM (`Puente --uninstall`).
9. Inicio con Windows: apagado salvo que ya estuviera activo; si lo estaba, entrada `Run` de tu usuario.

Sin internet: `Puente.exe --offline <carpeta>` con los archivos publicados (zip, manifiesto, firmas). Mismas comprobaciones.

## Migración desde `%LOCALAPPDATA%\Programs\Vigia` (0.20.x)

Las versiones publicadas (hasta 0.20.17) se actualizan descargando el `Puente.exe` de la última versión, comprobando su
firma con la misma clave pública, y abriéndolo con `--update --wait <pid>`. Ese Puente es el 2.0, así que **la actualización
normal de una 0.20.x ya es el instalador de transición**: no hace falta que el usuario descargue nada aparte.

Lo que hace Puente 2.0 al ver la instalación vieja:

1. Detecta `%LOCALAPPDATA%\Programs\Vigia\Vigia.exe` y su versión (de la cuenta del escritorio, aunque UAC se apruebe con otra).
2. **No confía en ningún ejecutable viejo**: no ejecuta ni copia nada de esa carpeta; instala desde el paquete firmado.
3. Instala en Program Files con los pasos 2–7 de arriba.
4. Tus datos están en `%LOCALAPPDATA%\Vigia` y **no se mueven**: ajustes, historial, copias y perfiles siguen en su sitio.
5. Tarea vieja `\Vigia\Iniciar Vigia` (la que abría Vigía como administrador desde una carpeta escribible): se lee su XML y
   **solo se borra si su ejecutable es un `Vigia.exe` de Vigía** (la carpeta vieja o la nueva). Si estaba activa, se crea la
   entrada `Run` de tu usuario en su lugar. Cualquier otra tarea con ese nombre no se toca.
6. Accesos directos viejos y la entrada de desinstalación de HKCU: se quitan; quedan los nuevos.
7. Las carpetas viejas (`Vigia`, `Vigia.anterior`, `Vigia.nueva`, `Vigia.cambio`, `Vigia-Puente`) se borran **solo después** de
   que la nueva pasó `--install-check` y está activa. Si alguna está en uso, queda en `PendingDelete` (solo rutas viejas
   conocidas) y se reintenta en el próximo arranque de Puente.
8. Queda una sola actualización: la vieja `Vigia-Puente` desaparece, la de Program Files es la única.

Mientras no se migre, Vigía 0.21 en la carpeta vieja abre, pero **el ayudante de administrador no arranca** (la carpeta no
está protegida) y la pantalla de actualizaciones ofrece «Actualizar» aunque sea la misma versión, para migrar.

## Sensores y PawnIO (versión pública)
- La temperatura del procesador se lee con **PawnIO**, el driver oficial firmado de su autor. Puente lo instala **solo si falta**, dentro del mismo permiso de administrador (UAC) de la instalación o actualización, y **no** con un juego abierto: en ese caso lo aplaza y queda escrito en `puente.log` («aplazado por juego abierto; reintentar con Reparar»). No hay un consentimiento aparte en el Monitor.
- Si Vigía instaló PawnIO (lo apunta en `pawnio.json`), al desinstalar Vigía se quita con su desinstalador oficial. Si ya estaba antes, se deja como estaba. Windows conserva el driver registrado y parado.
- El servicio «Sensores de Vigía» (SYSTEM) **no forma parte de la versión pública** mientras no tenga una revisión externa (`SENSOR_SERVICE_EXTERNAL_REVIEW.md`). `tools/release.sh` lo compila fuera y para si se cuela. Si una compilación de laboratorio lo dejó instalado, Puente lo quita al instalar, actualizar o reparar, antes de tocar PawnIO.
- Sin servicio, Vigía abre como tu usuario y no puede abrir el driver: el Monitor dice «Temperatura de CPU no disponible.» con el motivo, sin número ni «°C». Con el ayudante de administrador abierto (después de una tarea que lo pidió), el código lee los grados a través de él; **eso falta comprobarlo en un PC físico**.

## Actualizaciones

- Igual que la instalación nueva, en otra carpeta de `versiones\`: **nunca se sobrescribe una versión que esté abierta**, así
  que no hay problema con DLL bloqueadas. Quedan la versión activa y la anterior; las demás se borran cuando no están en uso.
- **Anti-downgrade**: `estado.json` guarda la más nueva vista (`highest`) y el piso de seguridad (`floor`, sale del campo
  `minVersion` del manifiesto firmado). Un manifiesto viejo bien firmado no hace volver atrás. Puente tampoco se reemplaza
  por uno más viejo, ni desde Puente (`PlacePuente`) ni desde Vigía (`EnsurePuenteAsync` compara versiones de archivo).
  `estado.json` está en la carpeta protegida: un programa sin permisos no lo puede bajar.
- `MakeManifest` rechaza un `minVersion` mayor que la versión publicada.

## Recuperación si algo falla

| Qué pasó | Qué hace Puente |
|---|---|
| Descarga cortada / sin conexión | Se reanuda en el siguiente intento (Range). Nada activo cambia. |
| Firma o huellas que no cuadran | Se borra lo descargado; Vigía sigue como estaba. |
| Falta espacio | Para antes de extraer, con el mensaje de cuántos MB hacen falta. |
| Error de permisos / antivirus bloquea un archivo | Para en «preparando»: la versión activa no se tocó; la carpeta `.preparando` se borra. |
| La versión nueva no pasa `--install-check` | No se activa. Sigue la anterior. |
| Corte de luz o reinicio a mitad | Al abrir Puente lee `diario.json`: si se cortó en «activando» y los archivos están bien, termina; si están mal, vuelve a la anterior **solo si está intacta y no está por debajo del piso**; en cualquier otra fase borra lo a medias. |
| Vigía instalada no abre o archivos cambiados | `Puente.exe --repair`: compara con `.vigia-archivos.json`, aparta la carpeta y la vuelve a poner desde el paquete firmado. |
| La versión nueva da problemas | `Puente.exe --rollback`: vuelve a la anterior si sus archivos siguen intactos y no es insegura (código 2 = por debajo del piso, 3 = dañada). |
| Migración a medias | La instalación vieja no se borra hasta que la nueva está activa y comprobada: se puede seguir usando o volver a abrir Puente para terminar. |

Ninguna recuperación borra `%LOCALAPPDATA%\Vigia` ni `Documentos\Vigía`. Desinstalar (`Puente --uninstall`) quita la
instalación, accesos, entrada de Apps, `Run` y la tarea vieja si es de Vigía; **tus datos se quedan**.

Recuperación manual si Puente mismo no abre (como administrador): borrar `%ProgramFiles%\Vigia` y volver a ejecutar
`Vigia-Setup.exe`. Los datos del usuario no están ahí.

## Validar en una VM (antes de publicar)

Requisitos: VM con Windows 11 (y otra con Windows 10), **UAC activado**, una cuenta administradora y una estándar, snapshot.

1. **Instalación vieja**: instalar 0.20.17 (release publicada) y activar «Abrir al iniciar Windows». Crear algunos ajustes,
   una copia y un historial de Apps. Snapshot `vieja`.
2. **Migración**: publicar 0.21.0 en un repo de pruebas o copiar los artefactos y usar `Puente.exe --offline <carpeta>`.
   Actualizar desde la Vigía 0.20.17. Comprobar:
   - `Get-ScheduledTask -TaskPath '\Vigia\'` → vacío; `HKCU\...\Run` tiene `Vigia` con `"…\versiones\0.21.0\Vigia.exe" --tray`.
   - `%LOCALAPPDATA%\Programs\Vigia*` desaparecieron; `%LOCALAPPDATA%\Vigia` intacto (ajustes, copias, historial).
   - `icacls "%ProgramFiles%\Vigia" /T` → solo SYSTEM y Administradores con (F), Usuarios y ALL APPLICATION PACKAGES con (RX);
     `Get-Acl` → dueño BUILTIN\Administrators; ningún `<JUNCTION>`/`<SYMLINKD>` en `dir /AL /S`.
   - Reiniciar: Vigía arranca en la bandeja **sin aviso de UAC**, una sola instancia.
3. **Usuario estándar** (iniciar sesión con la cuenta estándar): intentar crear, cambiar, renombrar o borrar en
   `%ProgramFiles%\Vigia` y en `.descargas` → acceso denegado. Abrir Vigía: abre sin UAC; las lecturas funcionan; una acción
   de administrador pide credenciales de administrador → el ayudante **no arranca** con otra cuenta (comportamiento previsto).
4. **Ayudante**: como admin, aplicar un ajuste, limpiar, quitar una app de prueba, restaurar una copia. Un solo UAC por sesión.
   Editar a mano una copia en `%LOCALAPPDATA%\Vigia\copias` (cambiar un destino) → la restauración la rechaza con explicación.
5. **Corte**: durante una actualización, apagar la VM en seco en «preparando» y en «activando». Al arrancar, abrir Puente →
   queda la versión anterior o termina la nueva; nunca una mezcla. `--repair` y `--rollback`.
6. **Anti-downgrade**: ofrecer un manifiesto firmado más viejo → rechazado.
7. **Desinstalar** desde «Aplicaciones instaladas» → sin restos en Program Files, ProgramData (salvo el registro), Run ni
   tareas; `%LOCALAPPDATA%\Vigia` sigue.
8. Volver al snapshot `vieja` y repetir 2 sin internet (`--offline`).

Lo que ya cubren las pruebas automáticas (`node tools/puente-test.js`, 57 comprobaciones, en `%TEMP%\vigia-test-xxxxxxxx`
con un Vigía de prueba y un usuario sin permisos simulado con un token SAFER): instalación nueva, permisos, enlaces,
TOCTOU en `.descargas`, manifiesto viejo, formato viejo sin manifiesto, DLL plantada, reparación, vuelta atrás (también
con la anterior dañada), corte a mitad, migración con tarea y accesos viejos, datos intactos y desinstalación. Esto
**no sustituye** a la VM: no hay UAC real, ni Program Files real, ni reinicios.
