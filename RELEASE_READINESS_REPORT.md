# Release readiness — NOT READY · SIGNING_BLOCKED

Fecha: 2026-10-30 · rama `debloat-v2` · solo commits locales (sin push, merge ni release).
Candidato local, notas, instalación, migración, recuperación y checklist de seguridad: **`RELEASE_CANDIDATE_0.21.0.md`**. Resumen completo: `FINAL_VALIDATION_REPORT.md`.

## Ejecución verificada hoy (no se reutilizan resultados viejos)
| Prueba | Resultado |
|---|---|
| Suite `s26` sobre `9459165` (2026-10-30 01:20) | builds Vigía/Puente/Puente-test rc 0, 0 warnings · selftest 361/0 · Puente 76/0 · release 31/0 · contrato OK · i18n pt/fr/de 2435 claves 0 errores · Apps 44/0 · Monitor 71/0 · LabGuard PC 24/0 · guarda 10/0 · `--sensor-service` rc 2 · 0 claves en logs |
| Candidato `VIGIA_RELEASE_SIGN=fuera` desde `9459165` | rc 0 · selftest del empaquetado 361/0 · sin firmar (la clave no se tocó) · zip sin claves ni servicio |
| VigiaLab 30-10 (filas 33–34) | tarea administrativa de 11 min con UAC real: sin cortes, cierre 600 s tras el fin, re-pedido y «No» limpios · `estandar`: ayudante rc 3, Program Files denegado, 0 procesos |
| VigiaLab 0.21.8 (clave de laboratorio) | servicio quitado antes de PawnIO; abre sin UAC; Monitor «N/D» sin °C (fila 32) |

Actualización 30-10 17:40: pruebas afectadas sobre `7202134` (selftest 362/0, release 31/0, contrato OK, firma 36/0 con claves de prueba); VigiaLab filas 35–37; candidato sin firmar `d8e24d9`. Decisiones en `OWNER_DECISIONS_0.21.0.md`.

## Por qué no está lista
1. **Clave de firma** (`SIGNING_BLOCKED`, P0): `CodexSandboxUsers` puede leer `~/.vigia` por ACL explícita y la clave está sin cifrar. Sin prueba de lectura, pero tampoco se puede descartar. Retirar el acceso, custodiarla en una cuenta de firma y decidir la rotación necesita tu autorización (`SECURITY_KEY_REVIEW.md`, A/B/C).
2. **Temperaturas en hardware físico** (HARDWARE_VALIDATION_PENDING): la VM no tiene sensores. Afecta además a una frase de las notas.
3. **UAC con usuario estándar en un PC físico** (HARDWARE_VALIDATION_PENDING / NEEDS_OWNER_APPROVAL).
4. **Servicio SYSTEM** (EXTERNAL_AUDIT_PENDING): fuera de 0.21.0. No bloquea 0.21.0, pero sí cualquier versión que lo incluya.
5. **Decisión de publicar 0.21.0** (NEEDS_OWNER_APPROVAL), y aprobar los cambios de comportamiento: cierre del ayudante a los 10 min, texto de temperatura y servicio fuera del paquete.

La app **funciona sin el servicio de sensores**: la dependencia se quitó del empaquetado sin romper nada (selftest, interfaz y VM). Ese punto ya no bloquea.
