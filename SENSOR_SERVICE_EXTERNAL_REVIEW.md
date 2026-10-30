# Revisión externa independiente — servicio «Sensores de Vigía»

Estado: **EXTERNAL_AUDIT_PENDING**. Este documento es el encargo para un revisor externo. No es una auditoría.

`SENSOR_SERVICE_AUDIT.md` es una **revisión interna** hecha con herramientas de programación asistida durante el
desarrollo; no cuenta como independiente y no sustituye a esta revisión.

## Decisión para la versión pública

Mientras esta revisión no esté hecha y aprobada por el propietario, **el servicio no va en la versión pública**:

| Dónde | Cómo queda fuera | Comprobación |
| --- | --- | --- |
| Compilación | `VigiaSensores` solo existe con `-p:SensorService=true` (define `SENSOR_SERVICE`); por defecto no | `tools/sensor-install-contract-test.py` |
| Vigía | Sin el símbolo, `Vigia.exe --sensor-service` es una orden desconocida (rc 2) y la interfaz no lee la memoria compartida | selftest «servicio de sensores en esta compilación: no»; rc 2 comprobado |
| Puente | No registra el servicio; si una compilación de laboratorio lo dejó registrado, lo quita al instalar o actualizar | contrato + VigiaLab |
| `tools/release.sh` | Compila con `-p:SensorService=false` (ignora la variable de entorno) y **no publica** si `--sensor-service` no da rc 2 | contrato |

Sin el servicio, Vigía funciona igual salvo la temperatura del procesador sin permisos de administrador: el Monitor
dice «Temperatura de CPU no disponible.» con el motivo, sin unidad ni valor inventado (el servicio solo publica la
temperatura del procesador; el resto de lecturas no pasa por él). Comprobado el 30-10-2026 en VigiaLab con el
candidato de laboratorio 0.21.0 (`7202134`): sin servicio, sin UAC al abrir, Monitor «N/D» + «Temperatura de CPU no
disponible.», sin «°C» en la tarjeta del procesador.

## Alcance pedido al revisor

Código en la rama `debloat-v2` (commit indicado en la entrega):

- `app/Puente/SensorService.cs` — registro, ACL, arranque, parada, recuperación y borrado del servicio.
- `app/Puente/Setup.cs` — instalación, actualización, reparación, rollback, desinstalación, `InstallTrust`, PawnIO.
- `app/Vigia/SensorHost.cs` — proceso del servicio (`--sensor-service`), memoria compartida `Global\VigiaSensores`.
- `app/Vigia/Telemetry.cs` — lector en la interfaz.
- `app/Vigia/Pawn.cs` — detección e instalación de PawnIO (driver oficial firmado, no modificado).
- `app/Vigia/Elevation.cs`, `AdminOps.cs` — ayudante con permisos (relacionado: alternativa al servicio).

### 1. Arquitectura
Confirmar que el servicio no tiene canal de entrada (sin tubería, socket, RPC, COM, archivos ni registro de
configuración) y que su única salida es la memoria compartida de 64 bytes (`VGS1`, versión 1).

### 2. Modelo de amenazas
Atacante = usuario estándar local o proceso sin privilegios en la misma sesión. Objetivos a evaluar: ejecutar código
como SYSTEM, cargar DLL en el servicio, reemplazar su ejecutable, reapuntar el servicio, suplantar la memoria
compartida para engañar a la interfaz, denegación de servicio, uso de PawnIO como primitiva de lectura/escritura física.

### 3. ACL
`C:\Program Files\Vigia` y `versiones\<v>\` (sin escritura para Usuarios, Usuarios autenticados ni Todos), descriptor
del servicio en el SCM, DACL de la memoria `O:SY D:P(A;;GA;;;SY)(A;;GR;;;BA)(A;;GR;;;AU)`, `ImagePath` con comillas.

### 4. Privilegios SYSTEM
Justificación de LocalSystem frente a alternativas; token con todos los privilegios (riesgo R1 de la revisión interna:
recomendación `sc privs` mínima, sin implementar).

### 5. PawnIO
Que Vigía solo use el driver oficial firmado, sin cargar otros drivers; qué módulos se cargan; quién puede abrir el
dispositivo; comportamiento al desinstalar (solo se quita si lo instaló Vigía).

### 6. IPC
Lector: dueño SYSTEM/Administradores exigido, cabecera, secuencia, frescura (≤15 s), rango 1–150 °C. Caso conocido: un
usuario estándar puede crear `Global\VigiaSensores` si el servicio está parado; el lector lo ignora por el dueño
(probado en VigiaLab). Evaluar condiciones de carrera al arrancar y parar.

### 7. Pruebas de abuso a repetir por el revisor
1. Como usuario estándar: escribir en la carpeta de instalación, reemplazar `Vigia.exe`, plantar una DLL junto al
   ejecutable, reconfigurar o parar el servicio (`sc config`, `sc stop`).
2. Crear `Global\VigiaSensores` con el servicio parado y comprobar que la interfaz no muestra el valor plantado.
3. Matar el proceso del servicio y comprobar recuperación; actualizar con el servicio colgado.
4. Instalar en carpeta con ACL débil y comprobar que el servicio no arranca (`InstallTrust`).
5. Fuzzing del lector con contenido arbitrario en la memoria compartida (cabecera, secuencia, hora, NaN, ±∞).
6. Revisar que ningún argumento, variable de entorno o archivo cambie lo que hace el proceso SYSTEM.

### 8. Riesgos residuales a valorar
Código de terceros dentro del proceso SYSTEM (biblioteca de sensores y módulos de PawnIO), token completo, sin
validación en hardware físico.

## Entregables esperados
Informe firmado por el revisor con: alcance real cubierto, hallazgos con gravedad, pruebas ejecutadas y su salida,
riesgos aceptables o no, y recomendación explícita sobre incluir el servicio en una versión pública.

## Criterios de independencia
Persona u organización ajena al desarrollo, sin acceso a las claves de firma, que ejecute sus propias pruebas en su
propio equipo o VM. Las revisiones hechas por los agentes de programación usados en el proyecto no cuentan.

## Lo que el revisor no recibe
Claves de firma de producción, credenciales de la VM de laboratorio ni datos del PC del propietario.

## Paquete de entrega al revisor (preparado el 30-10-2026; revisión NO hecha)

Commit de referencia: `7202134` (rama `debloat-v2`, sin publicar). Huella SHA-256 de cada archivo en ese commit
(primeros 16 caracteres; el revisor debe recalcularlas con `git show 7202134:<archivo> | sha256sum`):

| Archivo | Líneas | SHA-256 |
| --- | --- | --- |
| `app/Puente/SensorService.cs` | 104 | `a09dc878fe31fd73…` |
| `app/Puente/Setup.cs` | 1128 | `7a03446ae22b615d…` |
| `app/Puente/Program.cs` | 514 | `f1b7c232a9df87bc…` |
| `app/Puente/Puente.csproj` | 31 | `2fe4327bbea8e0d8…` |
| `app/Vigia/SensorHost.cs` | 353 | `d92546d8cb3b8bef…` |
| `app/Vigia/Telemetry.cs` | 325 | `d7b83d85b6e594ae…` |
| `app/Vigia/Pawn.cs` | 113 | `a18cfc49c8f2d90f…` |
| `app/Vigia/Elevation.cs` | 610 | `2df430cce409e96f…` |
| `app/Vigia/AdminOps.cs` | 379 | `b261506bf3263874…` |
| `app/Vigia/LabGuard.cs` | 52 | `3d6b2ce71e212bdc…` |
| `app/Vigia/Vigia.csproj` | 38 | `07fad741fa21bf42…` |
| `tools/sensor-install-contract-test.py` | 64 | `052468dd9bc4ab75…` |
| `tools/ui-monitor-test.js` | 79 | `9b769f9d9ada9286…` |
| `SENSOR_SERVICE_AUDIT.md` | 81 | `b63d6ca073c03aff…` |
| `CPU_SENSOR_INTEGRATION_REPORT.md` | 67 | `036c4296853d6557…` |
| `docs/PRIVILEGES.md` | 100 | `9617ff20548bc8fc…` |
| `docs/SECURITY-DESIGN.md` | 79 | `ef7e05a7591175a4…` |

### Cómo compilar la variante con servicio (solo en la VM del revisor)

```bash
dotnet build app/Vigia/Vigia.csproj   -c Release -r win-x64 -p:SensorService=true
dotnet build app/Puente/Puente.csproj -c Release -r win-x64 -p:SensorService=true -p:PuenteTest=true
```

`PuenteTest=true` permite que el revisor use **su propia** clave de prueba (`PUENTE_TEST_PUBKEY`); no recibe la de
producción. La compilación pública es la misma sin esos dos parámetros.

### Pruebas internas existentes (referencia, no cuentan como independientes)

| Prueba | Qué cubre | Resultado interno |
| --- | --- | --- |
| `python -I tools/sensor-install-contract-test.py` | el servicio no entra en la compilación pública; `release.sh` lo exige | OK en `7202134` |
| `node tools/ui-monitor-test.js` | sin lectura válida: «no disponible», sin °C, sin 0 °C | 71/0; prueba inversa 45 FAIL |
| `Vigia.exe --sensor-service` (pública) | orden desconocida | rc 2 |
| VigiaLab `squat.ps1` | usuario estándar crea `Global\VigiaSensores` antes que el servicio | objeto ajeno ignorado |
| Revisión interna `SENSOR_SERVICE_AUDIT.md` | 7 fallos de robustez corregidos (`791fdc9`) | interna, no independiente |

### Preguntas concretas para el revisor

1. ¿Puede un usuario estándar hacer que el servicio SYSTEM lea, escriba o ejecute algo distinto de publicar la
   temperatura del procesador? (no debe aceptar órdenes)
2. ¿La ACL de `Global\VigiaSensores` y la comprobación de dueño impiden suplantar o envenenar la lectura?
3. ¿La carpeta de instalación y el binario del servicio son inmodificables para usuarios no administradores
   durante instalar, actualizar, reparar, rollback y desinstalar (incluidas carreras)?
4. ¿La carga de PawnIO (driver oficial firmado, sin modificar) añade superficie que deba aceptarse explícitamente?
5. ¿Qué pasa si el servicio queda registrado y luego se instala una versión sin servicio? (debe borrarse)
6. Recomendación: incluir / no incluir en versión pública, y condiciones.

Hasta recibir ese informe firmado y la aprobación del propietario: **servicio excluido de 0.21.0**.
