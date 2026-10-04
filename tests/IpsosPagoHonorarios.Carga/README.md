# Pruebas de carga y estrés

Herramienta para medir la aplicación con volumen y usuarios simultáneos parecidos a producción, sobre **SQL Server**.
Tiene dos comandos:

- `generar` llena una base **de prueba** con volumen realista: prestadores con cuenta validada, 12 meses de ciclos
  cerrados (planillas, boletas y transferencias), el ciclo abierto con planillas por área y los usuarios internos y
  del portal. Escribe `escenario.json` con los usuarios y la contraseña de prueba.
- `ejecutar` lanza usuarios virtuales por HTTP contra la aplicación publicada. Cada usuario tiene su propia sesión:
  Operaciones navega sus planillas, descarga Excel y reemplaza planillas; Finanzas revisa, ve pagos e historial;
  los prestadores ingresan al portal y suben su boleta (un PDF del SII generado al vuelo).

> Nunca contra la base productiva: `generar` exige una base vacía (recién creada con `database/BD_PagoIpsos.sql`)
> y crea miles de registros ficticios.

## 1. Preparar el ambiente de prueba

1. Crea una base de prueba con `database/BD_PagoIpsos.sql`. Este script ya activa READ_COMMITTED_SNAPSHOT.
2. Publica y arranca la aplicación contra esa base, en modo Production, con un usuario como `user_sql`.
3. Compila la herramienta: `dotnet build tests/IpsosPagoHonorarios.Carga -c Release`.

## 2. Generar el volumen

```
dotnet tests/IpsosPagoHonorarios.Carga/bin/Release/net10.0/IpsosPagoHonorarios.Carga.dll generar ^
  --conexion "Server=SERVIDOR_PRUEBA;Database=BD_PagoIpsos;User Id=user_sql;Password=...;TrustServerCertificate=True" ^
  --prestadores 3000 --meses 12 --lineas 250 --areas-extra 8 --escenario escenario.json
```

En Windows, la carpeta es `net10.0-windows10.0.19041.0`. Conviene hacer un respaldo de la base recién generada
para restaurarla antes de cada prueba: las pruebas suben boletas y reemplazan planillas.

## 3. Ejecutar

```
dotnet .../IpsosPagoHonorarios.Carga.dll ejecutar --url http://SERVIDOR:PUERTO --perfil carga --escenario escenario.json --informe resultados.md
```

| Perfil | Qué simula | Duración |
| --- | --- | --- |
| `humo` | 2 operativos, 1 de Finanzas y 5 prestadores: verifica que todo responde | 30 s |
| `carga` | Día de punta: un operativo por área, 2 de Finanzas y 60 prestadores con pausas humanas (2 a 6 s) | 5 min |
| `pico` | Último día de plazo de boletas: 300 prestadores entran casi a la vez, más la operación interna | 2 min |
| `estres` | Escalones x1, x2, x4… x32 sin pausa humana, hasta superar 10 % de errores o p95 de 10 s | hasta 6 min |
| `resistencia` | La carga de punta durante 20 minutos, para ver fugas de memoria o degradación | 20 min |

- `--pid <pid>`: si la aplicación corre en la misma máquina Linux, agrega su CPU y memoria al informe.
- Cada usuario virtual envía su propia IP en `X-Forwarded-For`, porque el límite de intentos de ingreso es por IP.

El informe muestra, por acción, el número de solicitudes, los errores con su motivo y la latencia en milisegundos:
p50 (mediana), p95, p99 y máximo.

## 4. Mirar SQL Server durante la prueba

Mientras corre, revisa estas vistas de SQL Server:

- `sys.dm_os_wait_stats`: si dominan `LCK_M_*`, hay bloqueos entre consultas. Revisa que READ_COMMITTED_SNAPSHOT esté
  activado; también lo muestra Administrador → Maestros → Parámetros.
- `sys.dm_exec_requests` con `blocking_session_id <> 0`: quién bloquea a quién.
- `sys.dm_db_missing_index_details`: índices que SQL Server sugiere.

Los resultados de la prueba hecha durante el desarrollo están en `docs/pruebas-de-carga.md`.
