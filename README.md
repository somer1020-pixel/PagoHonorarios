# IpsosPagoHonorarios

Aplicación web de **Pago de Honorarios** de Ipsos Chile (bloque 1 del macroproceso *Pagos y Rendiciones*): carga de producción y planillas por área, validación de cuentas bancarias, portal del prestador para subir la boleta del SII, revisión de Finanzas con devolución a 1 hora, diferimiento, aprobación, nómina, transferencias, cierre y archivo.

Especificación completa (alcance, roles, modelo, estados, reglas R-01 a R-28, formato Excel, despliegue y preguntas abiertas): [`docs/README-handoff.md`](docs/README-handoff.md).

> Todos los datos de ejemplo son ficticios. Lo marcado **TODO(diseño)** no está definido en la especificación y figura en las preguntas abiertas.

## Stack

| Capa | Tecnología |
|---|---|
| Runtime | .NET 10 (LTS), C# 14 |
| Web | ASP.NET Core Razor Pages (una página por pantalla) |
| UI | Bootstrap 5.3 + Bootstrap Icons 1.11 + IBM Plex (incluidos en `wwwroot/lib`, sin CDN) |
| Seguridad | ASP.NET Core Identity con roles (Operaciones, Finanzas, Prestador, Admin) |
| Datos | EF Core 10 + SQL Server Express (migraciones en `Data/Migraciones`); SQLite para desarrollo y pruebas |
| PDF | PdfPig (lectura de la boleta electrónica del SII) |
| Excel | ClosedXML (formato de Finanzas, plantilla oficial opcional) |
| Servidor | IIS + ASP.NET Core Hosting Bundle · Cloudflare Tunnel + Access |

```
src/IpsosPagoHonorarios.Core   Entidades, estados y reglas de negocio (sin NuGet)
src/IpsosPagoHonorarios.Web    Razor Pages, EF Core, Identity, ClosedXML, PdfPig, servicio de plazos (BackgroundService), TimeProvider
tests/IpsosPagoHonorarios.Tests  xUnit: reglas de Core, servicios sobre SQLite en memoria y la aplicación completa (WebApplicationFactory)
docs/                          Handoff de diseño
deploy/                        Ejemplos de configuración (cloudflared, appsettings de producción)
```

## Ejecutar en local (demo)

Requiere el SDK de .NET 10.

```bash
dotnet run --project src/IpsosPagoHonorarios.Web --launch-profile http
# http://localhost:5032
```

En `Development` usa SQLite (`App_Data/honorarios-dev.db`) y carga los **datos de demo** de la sección 11 (ciclo OCT-2026 con FACE TO FACE devuelta, MYSTERY SHOPPING en revisión y DATA PROCESSING con 5 alertas de cuenta; AGO y SEP-2026 cerrados con su ZIP). La semilla ejecuta el flujo real con los servicios: importación, lectura de PDFs, revisión, pago y cierre. Para empezar de cero, borra `App_Data`.

| Usuario | Rol | Contraseña |
|---|---|---|
| `andres.paredes@ejemplo.cl` | Operaciones | `Demo2026!` |
| `carolina.diaz@ejemplo.cl` | Finanzas | `Demo2026!` |
| `admin@ejemplo.cl` | Admin | `Demo2026!` |
| `17.345.120-2` (Valentina) | Prestador (portal) | `Demo2026!` |

En la pantalla Correcciones, el botón **Simular vencimiento (demo)** (solo en Development) equivale al ajuste *simularVencido* del mockup (R-12).

## Pantallas

| # | Ruta | Rol |
|---|---|---|
| 1 | `/Cuenta/Login`, `/Cuenta/Activar`, `/Cuenta/Recuperar` | Todos |
| 2 | `/` (Panel del ciclo) | Interno |
| 3 | `/Ciclos/Produccion` | Operaciones (Finanzas lee) |
| 4 | `/Ciclos/ValidacionCuentas` | Operaciones · Finanzas |
| 5 | `/Ciclos/Planilla` | Operaciones edita · Finanzas lee |
| 6 | `/Boletas/Seguimiento` | Operaciones · Finanzas |
| 7 | `/Ciclos/Correcciones` | Operaciones corrige · Finanzas acepta/reabre |
| 8 | `/Finanzas/Revision` | Finanzas |
| 9 | `/Finanzas/Pagos` | Finanzas |
| 10 | `/Maestros/Prestadores` | Operaciones · Finanzas |
| 11 | `/Maestros/Jobs` | Finanzas mantiene · Operaciones lee |
| 12 | `/Ciclos/Historial` | Interno |
| 13 | `/Portal` | Prestador (diseño para celular) |

Barra superior con selector de ciclo y la píldora **“Corrección vence en mm:ss”** cuando hay una devolución activa; menú lateral agrupado que en celular (≤ 760 px) se vuelve fila de chips. El rol Prestador solo accede a `/Portal` (cualquier otra ruta responde 403).

## Base de datos SQL Server (BD_PagoIpsos)

1. Ejecutar [`database/BD_PagoIpsos.sql`](database/BD_PagoIpsos.sql) en **AMCLSANSQL9** (SSMS o `sqlcmd -S AMCLSANSQL9 -U <admin> -P <clave> -i database/BD_PagoIpsos.sql`). Crea la base, las 29 tablas con sus índices, los roles, los catálogos, los parámetros y la tasa 2026. Es idempotente.
2. Dar permisos a `user_sql` (bloque comentado al inicio del script): `db_datareader`, `db_datawriter` y, si la app debe aplicar migraciones futuras, `db_ddladmin`.
3. La cadena de conexión está en `src/IpsosPagoHonorarios.Web/appsettings.json` (`ConnectionStrings:Default`) con la clave `XXXXX`. **No subas la clave real al repositorio (es público):** créala en `appsettings.Production.json` (ignorado por git; ver `deploy/appsettings.Production.ejemplo.json`) o en la variable de entorno `ConnectionStrings__Default`.
4. Primer administrador: definir `Semilla:AdminEmail` y `Semilla:AdminPassword` en ese mismo `appsettings.Production.json`; la app lo crea al iniciar.
5. En Visual Studio, el perfil **BD_PagoIpsos (SQL Server)** ejecuta la app contra esa base (`http://localhost:5033`). Los perfiles `http`/`https` siguen usando SQLite con datos de demo.

Para regenerar el script tras cambios del modelo: `dotnet ef migrations script --idempotent -p src/IpsosPagoHonorarios.Web` con `ASPNETCORE_ENVIRONMENT=Production`.

## Pruebas

```bash
dotnet test
```

66 pruebas cubren las 28 reglas, incluidos los mínimos de la especificación: RUT y montos (`ROUND(250 × 85,28) = 21.320`), los 6 resultados de R-24 con el caso DATA PROCESSING (2 coincide, 2 tipeo, 1 nueva, 1 distinta, 1 tercero), R-28 con sus 3 casos, conciliación con varias filas ($345.000), fecha límite día 10, plazo de 60 minutos, aprobación bloqueada con observaciones o cuentas sin validar, autorización del Prestador (no ve ni sube boletas de otro RUT) y el round-trip XLSX con `B3 = Σ H`.

| Archivo | Reglas |
|---|---|
| `Core/ReglasBasicasTests.cs` | R-01, R-03, R-04, R-05, formatos es-CL |
| `Core/CuentasTests.cs` | R-07, R-24, R-28 |
| `Core/ProduccionYBoletaTests.cs` | R-02, R-06, lectura del PDF (§9) |
| `Integracion/PlanillaYCuentasTests.cs` | R-02, R-08, R-16, R-21, R-23 a R-28, XLSX |
| `Integracion/RevisionYPagoTests.cs` | R-06, R-09 a R-19, R-21, R-22 |
| `Web/AplicacionTests.cs` | R-16, R-17, R-20, roles, restricción por host, bloqueo 5/15 |

## Configuración

| Clave | Uso |
|---|---|
| `ConnectionStrings:Default`, `Datos:Proveedor` | `SqlServer` (producción, aplica migraciones al iniciar) o `Sqlite` |
| `Almacenamiento:Ruta` | Carpeta de PDFs, XLSX por versión, nóminas, comprobantes y ZIP de cierre |
| `Plantillas:PlanillaFinanzas` | Ruta de la plantilla oficial de Finanzas; si no existe se genera una equivalente (hoja Formato, listas `banco` y `tipos_de_cuenta`) |
| `Publicacion:HostPrestadores` | Host público de prestadores (`boletas.<dominio>`): solo sirve `/Portal`, `/Cuenta` y estáticos |
| `Publicacion:IntentosPorMinuto` | Límite de POST de ingreso por IP (además del bloqueo 5 intentos / 15 min) |
| `Turnstile:SiteKey/SecretKey` | Cloudflare Turnstile opcional en el ingreso |
| `Semilla:AdminEmail/AdminPassword` | Crea el primer administrador si no existe |
| `Plazos:Habilitado` | Proceso en segundo plano que revisa vencimientos cada minuto (R-12) |

Los catálogos (áreas, glosas, bancos, tipos de cuenta y de gasto) se cargan desde `src/IpsosPagoHonorarios.Web/Data/Semilla/*.csv`. Contienen solo el extracto de la especificación: reemplázalos por la hoja Formato completa antes de producción.

Formato de la exportación del sistema (TODO(diseño), pregunta abierta 5): XLSX o CSV con títulos `Rut; Nombre; Job; Nombre Job; Glosa; Valor unitario; Cantidad; Tipo cuenta; Cuenta; Banco`. En CSV, decimales con coma (`85,28`); el punto es separador de miles.

## Despliegue (Windows Server)

1. Instalar IIS y el **ASP.NET Core Hosting Bundle (.NET 10)**; SQL Server Express.
2. `dotnet publish src/IpsosPagoHonorarios.Web -c Release -o publish` (o usar el artefacto del workflow) y crear el sitio en IIS apuntando a `publish`.
3. Configurar `appsettings.Production.json` (ver `deploy/appsettings.Production.ejemplo.json`). La app aplica las migraciones al iniciar; también se puede usar el script idempotente `migraciones.sql` del artefacto.
4. Instalar `cloudflared` como servicio con `deploy/cloudflared-config.yml`: `honorarios.<dominio>` detrás de Cloudflare Access (equipo interno) y `boletas.<dominio>` sin Access (prestadores).
5. TODO(diseño): proveedor SMTP. Los avisos se encolan en la tabla `Correos` (bandeja de salida) y quedan en el log.

GitHub Actions (`.github/workflows/ci.yml`) compila y prueba en cada push; en `main`, si todo pasa, publica el artefacto para IIS con el script de migraciones.

## Pendientes de definición (TODO(diseño))

Supuesto bruto vs. líquido (R-04), formato de nómina bancaria, día de pago, PDFs reales anonimizados, formato de exportación del sistema de encuestas, logo oficial, RUT y razón social de la empresa receptora (hoy `77.777.777-7` ficticio en `Parametros`), códigos BANEFE/SANTANDER (37), glosa de MYSTERY SHOPPING, estados de SolicitudCorreccion, si el reenvío exige 0 observaciones abiertas (hoy se permite reenviar con pendientes), política de contraseña (provisoria: 8 caracteres con minúscula y número), fuente del dato “boleta anulada”, umbral de “cuenta muy distinta” y proveedor de correo/almacenamiento. Detalle en `docs/README-handoff.md` §14.
