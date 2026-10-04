# Pruebas de carga y estrés (SQL Server)

Fecha: 4 de octubre de 2026. Herramienta: [`tests/IpsosPagoHonorarios.Carga`](../tests/IpsosPagoHonorarios.Carga/README.md).

## Resumen

- **Antes**, con 74 usuarios simultáneos en un día de punta, la aplicación fallaba. Hubo un 2,6 % de errores y el p95 llegó a 30 s.
  Los paneles, la página de Pagos y la subida de boletas se quedaban esperando bloqueos de SQL Server hasta agotar el
  tiempo de espera (30 s).
- **Después**, con la misma carga, hubo 0 errores y un p95 de 128 ms. La aplicación atendió 2,6 veces más solicitudes.
- **Acción obligatoria para el DBA:** ejecutar `database/07_LecturaSinBloqueos.sql`, que activa `READ_COMMITTED_SNAPSHOT`.
  Es el cambio con más efecto. `user_sql` no tiene permiso para hacerlo.
- El estrés sin pausas sostuvo unas 110 solicitudes por segundo. El punto de quiebre (p95 > 10 s) llegó a los 1.024
  usuarios virtuales sin pausa. Eso equivale a varias veces la carga real esperada.

## Ambiente

Todo corrió en una sola máquina virtual: SQL Server 2022 (Developer, en Docker), la aplicación publicada en modo
Production y el generador de carga. La máquina tenía 4 vCPU Xeon de 2,1 GHz y 15 GB de RAM. La app se conectó como
`user_sql`, con los permisos `db_datareader`, `db_datawriter` y `db_ddladmin`.
Los tres componentes compiten por la misma CPU, así que las cifras son **pesimistas**. Con la app y SQL Server en
servidores separados, deberían mejorar.

Volumen generado:

- 3.000 prestadores con cuenta validada y 1.064 con acceso al portal.
- 20 áreas, incluidas las de CEX, Public, BHT, MSU y AUM, con un usuario operativo por área.
- 12 ciclos cerrados (planillas, boletas, transferencias y auditoría).
- El ciclo abierto OCT-2026, con planillas en Borrador y EnRevision.

Qué hace cada usuario virtual:

| Usuario | Acciones |
| --- | --- |
| Operaciones | Panel, planilla, seguimiento, validación, producción, búsqueda de prestadores, descarga XLSX y reemplazo de su planilla con un CSV |
| Finanzas | Revisión, Pagos, panel, historial, Excel de la planilla enviada y correcciones |
| Prestador | Ingresa al portal y sube su boleta, un PDF del SII generado al vuelo |

## Resultados: carga de punta

El escenario reúne a 12 operativos, 2 personas de Finanzas y 60 prestadores durante 5 minutos, con pausas de 2 a 6 s
entre acciones.

| Versión | Solicitudes | Por segundo | Errores | p50 | p95 | p99 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Antes | 4.116 | 13,7 | 2,60 % | 46 ms | 29.957 ms | 36.654 ms |
| Solo cambios de código | 5.648 | 18,8 | 1,63 % | 43 ms | 27.155 ms | 31.038 ms |
| Código + RCSI | 10.663 | 35,5 | 0,16 % | 24 ms | 103 ms | 168 ms |
| **Final** | **10.873** | **36,2** | **0 %** | **27 ms** | **128 ms** | **212 ms** |

p95 por acción, antes y después:

| Acción | Antes | Final |
| --- | ---: | ---: |
| Finanzas → Pagos | 60.001 ms (timeouts) | 175 ms |
| Operaciones → Panel | 60.001 ms (timeouts) | 119 ms |
| Finanzas → Historial | 3.708 ms | 312 ms |
| Maestros → Prestadores | 31.530 ms | 140 ms |
| Portal → subir boleta | 34.181 ms (77 errores) | 197 ms |
| Producción → importar planilla | 1.473 ms | 2.500 ms |

En la versión final se subieron 1.796 boletas sin errores. La importación tarda más que antes porque ahora espera su
turno si alguien está escribiendo en la misma planilla. Antes, ese mismo choque terminaba en un error o en un deadlock.

## Resultados: pico de boletas

Este escenario simula el último día de plazo: 300 prestadores entran casi a la vez durante 2 minutos, junto con la
operación interna.

| Versión | Por segundo | Errores | p50 | p95 |
| --- | ---: | ---: | ---: | ---: |
| Antes de la carga filtrada de la planilla | 62,6 | 0 % | 2.019 ms | 13.474 ms |
| **Final** | **115,2** | **0 %** | **905 ms** | **4.704 ms** |

En el pico, la CPU promedió 308 % de 400 %, así que el límite fue la CPU. Dos tiempos dominan:

- **Ingreso al portal** (p50 2,9 s): el hash de contraseñas PBKDF2 es costoso a propósito, unos 80 ms de CPU por
  ingreso.
- **Subida de boleta** (p50 3,0 s): lee el PDF, concilia y guarda la boleta.

## Resultados: estrés

Los escalones duplican usuarios sin pausa humana hasta que hay 10 % de errores o un p95 de 10 s.

| Escalón | Usuarios virtuales | Por segundo | p95 |
| --- | ---: | ---: | ---: |
| x1 | 27 | 84,1 | 522 ms |
| x2 | 64 | 112,0 | 957 ms |
| x4 | 128 | 117,8 | 2.520 ms |
| x8 | 256 | 111,8 | 5.169 ms |
| x16 | 512 | 108,8 | 9.515 ms |
| x32 | 1.024 | 104,0 | 20.143 ms ← quiebre |

- El rendimiento se estabiliza en unas 110 solicitudes por segundo. Al sumar más usuarios solo crece la espera; el
  servidor no colapsa.
- El cliente no registró errores. En el servidor hubo 2 timeouts de SQL en x32.
- Antes de la carga filtrada, el techo era de unas 75 solicitudes por segundo y el quiebre llegaba en x16.
- En estrés, “boletas que cuadran” baja porque las importaciones de la prueba cambian los montos de las planillas.
  Es un efecto de la prueba, no un error.

## Causas encontradas y correcciones

1. **Lecturas bloqueadas por escrituras.** Esta era la causa principal.
   - Las vistas DMV de SQL Server mostraban esperas `LCK_M_S` y `LCK_M_IX`. Las consultas de los paneles (que leen
     todas las líneas y boletas del ciclo) y los contadores del menú quedaban bloqueados detrás de los INSERT de
     boletas, y al revés.
   - Corrección: `READ_COMMITTED_SNAPSHOT` (RCSI). Con esa opción, las lecturas ven la última versión confirmada sin
     esperar.
   - Se agregaron el script `07_LecturaSinBloqueos.sql`, el intento de activarlo al iniciar la app (deja una
     advertencia en el log si no tiene permiso) y el indicador en Maestros → Parámetros.
2. **Consultas que cargaban el ciclo completo en memoria.**
   - Pagos cargaba todas las líneas del ciclo para contar las pendientes; ahora usa `COUNT` en SQL.
   - Historial usa agregados en SQL: conteos, prestadores distintos, sumas y última transferencia.
   - Los contadores del menú (alertas de cuenta y boletas pendientes) se calculan con `COUNT DISTINCT` y `NOT EXISTS`.
   - Maestros → Prestadores muestra como máximo 100 filas y pide buscar por nombre o RUT.
3. **Deadlocks y errores de concurrencia** entre el reemplazo de una planilla y las boletas que subían sus prestadores.
   - Las escrituras sobre una misma planilla se serializan con
     `UPDATE Planillas SET Version = Version WHERE Id = @id` dentro de la transacción.
   - Si aun así hay choque (deadlock 1205 o error de concurrencia), el usuario ve el mensaje “Otra persona modificó
     esta planilla al mismo tiempo. Vuelve a intentarlo.” en vez de un error 500.
4. **La subida de boleta cargaba la planilla entera.** Ahora carga solo las líneas y boletas del prestador que sube.
   El PDF y el OCR se procesan fuera de la transacción, para que el bloqueo dure lo mínimo.
5. **Índices de cobertura.**
   - `LineasPago (PlanillaId, Estado)` con columnas incluidas.
   - `Boletas (PlanillaId, PrestadorId)` con `Estado` incluido.
   - Están en la migración `IndicesCarga` y en el script `08_IndicesCarga.sql`.
6. **Scripts SQL con sqlcmd.** `sqlcmd` usa `QUOTED_IDENTIFIER OFF` por defecto, y con eso fallaban los índices
   filtrados de Identity y se revertía todo el script. Todos los scripts ahora parten con `SET QUOTED_IDENTIFIER ON`.

## Recomendaciones para producción

- **DBA:** ejecutar `database/07_LecturaSinBloqueos.sql` y confirmar en Maestros → Parámetros que diga “activado”.
- **Servidores separados:** la app y SQL Server en máquinas distintas. Para el pico de boletas, conviene que la app
  tenga al menos 4 núcleos, porque en ese escenario el límite es la CPU.
- **Límite de intentos de ingreso:** es de 10 por minuto por IP. Si muchos usuarios salen a internet por la misma IP
  (una oficina detrás de NAT), pueden recibir “demasiados intentos” (HTTP 429) en el día de punta. Conviene revisar
  ese valor.
- **Proxy inverso:** la app confía hoy en el encabezado `X-Forwarded-For` de cualquier origen. En producción hay que
  configurar `KnownProxies` con la IP del proxy o balanceador real, para que nadie pueda falsear su IP y saltarse el
  límite de ingresos.
- **Antes de cada cambio grande:** repetir el perfil `carga` sobre una copia de la base, nunca sobre la base
  productiva.
