# Despliegue en IIS (servidor AMCLSANSQL9)

Guía para publicar la aplicación en el IIS del mismo servidor donde está SQL Server. Pensada para una primera prueba en
producción y para las actualizaciones siguientes.

## Antes de empezar

En el servidor:

- **IIS** instalado, con la característica **Application Initialization** (Administrador del servidor → Agregar roles y
  características → Servidor web → Desarrollo de aplicaciones → Inicialización de aplicaciones).
- **ASP.NET Core Hosting Bundle (.NET 10)**, instalado **después** de IIS. Luego ejecuta `iisreset`.
- Acceso de administrador y el paquete: el artefacto **`IpsosPagoHonorarios-web`** de la última ejecución verde de
  [GitHub Actions](https://github.com/somer1020-pixel/PagoHonorarios/actions) (zip con la aplicación y `migraciones.sql`).

## 1. Base de datos (el DBA)

La aplicación se conecta con el usuario SQL `user_sql`. Con un usuario administrador:

1. **Base nueva:** ejecutar `database/BD_PagoIpsos.sql`. Ya crea las tablas, las migraciones y activa
   `READ_COMMITTED_SNAPSHOT`.
   **Base que ya existía:** ejecutar `migraciones.sql` del artefacto (es idempotente: solo agrega lo que falta).
2. Ejecutar `database/07_LecturaSinBloqueos.sql` (desde `master`). Activa las lecturas sin bloqueo. **Es obligatorio**: sin
   él, con varios usuarios las consultas se bloquean y aparecen errores. Es seguro repetirlo.
3. Dar permisos a `user_sql` sobre `BD_PagoIpsos`: `db_datareader`, `db_datawriter` y `db_ddladmin` (este último permite
   que la aplicación aplique sola las migraciones futuras al iniciar).

## 2. Carpetas

```powershell
New-Item -ItemType Directory D:\IpsosPagoHonorarios\app, D:\IpsosPagoHonorarios\archivos, D:\IpsosPagoHonorarios\app\logs -Force
```

- `app`: la aplicación.
- `archivos`: boletas en PDF, planillas, nóminas, comprobantes y ZIP de cierre. **Hay que respaldarla**.

Descomprime el artefacto dentro de `D:\IpsosPagoHonorarios\app` (el `web.config` debe quedar directamente ahí, no en una
subcarpeta).

## 3. Configuración

Crea `D:\IpsosPagoHonorarios\app\appsettings.Production.json` a partir de
`deploy/appsettings.Production.ejemplo.json`. Este archivo **no está en el paquete ni en el repositorio** a propósito:
lleva secretos.

```json
{
  "ConnectionStrings": {
    "Default": "Server=AMCLSANSQL9;Database=BD_PagoIpsos;User Id=user_sql;Password=<clave-real>;TrustServerCertificate=True;Encrypt=True;MultipleActiveResultSets=True"
  },
  "Datos": { "Proveedor": "SqlServer" },
  "Almacenamiento": { "Ruta": "D:\\IpsosPagoHonorarios\\archivos" },
  "Publicacion": { "HostPrestadores": "", "IntentosPorMinuto": 10 },
  "Semilla": { "AdminEmail": "admin@<dominio>", "AdminPassword": "<clave-inicial>", "Demo": false }
}
```

- `Semilla` crea el primer administrador al iniciar. Cambia la contraseña al ingresar la primera vez.
- Si ya tienes la plantilla oficial de Finanzas, agrega `"Plantillas": { "PlanillaFinanzas": "D:\\...\\Planilla Finanzas.xlsx" }`.
- Para WhatsApp, ver [`whatsapp.md`](whatsapp.md). Se puede dejar para después.

## 4. IIS

En PowerShell, como administrador:

```powershell
Import-Module WebAdministration
$pool = "IpsosPagoHonorarios"
New-WebAppPool $pool
Set-ItemProperty IIS:\AppPools\$pool -Name managedRuntimeVersion -Value ""            # Sin código administrado
Set-ItemProperty IIS:\AppPools\$pool -Name startMode -Value AlwaysRunning
Set-ItemProperty IIS:\AppPools\$pool -Name processModel.idleTimeout -Value "00:00:00"  # nunca se duerme
New-Website -Name $pool -PhysicalPath D:\IpsosPagoHonorarios\app -ApplicationPool $pool -Port 8080
Set-ItemProperty IIS:\Sites\$pool -Name applicationDefaults.preloadEnabled -Value $true
```

**¿Por qué no se debe dormir?** Dentro de la aplicación corre un proceso que revisa cada minuto los plazos de corrección
vencidos (y, si está activo, envía los WhatsApp). Si IIS apaga el grupo de aplicaciones por inactividad (20 minutos por
defecto), esos procesos se detienen hasta la siguiente visita.

## 5. Permisos

```powershell
$id = "IIS AppPool\IpsosPagoHonorarios"
icacls D:\IpsosPagoHonorarios\app      /grant "${id}:(OI)(CI)RX"
icacls D:\IpsosPagoHonorarios\app\logs /grant "${id}:(OI)(CI)M"
icacls D:\IpsosPagoHonorarios\archivos /grant "${id}:(OI)(CI)M"
# El archivo con secretos: solo administradores y la aplicación
icacls D:\IpsosPagoHonorarios\app\appsettings.Production.json /inheritance:r /grant "Administrators:F" "${id}:R"
```

## 6. Primera prueba

1. En el servidor, abre `http://localhost:8080/Cuenta/Login`. La primera carga tarda unos segundos (aplica migraciones).
2. Ingresa con el administrador de `Semilla` y cambia su contraseña.
3. Revisa en **Maestros → Parámetros**:
   - **Base de datos · lecturas sin bloqueo**: debe decir *activado*. Si dice *DESACTIVADO*, falta el paso 1.2.
   - **OCR de boletas sin texto → Probar OCR**: debe reconocer la boleta de prueba.
   - **Avisos por WhatsApp**: *Desactivado* mientras no lo configures.
4. Crea un usuario de Operaciones y uno de Finanzas en **Maestros → Usuarios**, y haz un recorrido corto: importar una
   planilla, subir una boleta, revisar.

### Si no abre

- **HTTP 500.30 / 502**: en `web.config`, cambia `stdoutLogEnabled="false"` a `"true"` y revisa `app\logs\stdout_*.log`.
  Causas típicas: Hosting Bundle sin instalar (o sin `iisreset`), cadena de conexión incorrecta, o permisos del paso 5.
- **Error al conectar con SQL**: prueba el mismo usuario y clave con `sqlcmd -S AMCLSANSQL9 -U user_sql -d BD_PagoIpsos`.
- **Dice que falta una tabla o columna**: falta aplicar las migraciones (paso 1) o `user_sql` no tiene `db_ddladmin`.

### OCR en el servidor

La lectura de boletas sin texto (PDF compartidos desde la app del SII) usa primero el OCR de Windows y, si no está
disponible, Tesseract. **No se probó en Windows Server con la identidad del grupo de aplicaciones**: confírmalo con
*Probar OCR*. Si falla, prueba con **Cargar perfil de usuario = True** en la configuración avanzada del grupo de
aplicaciones y, si sigue fallando, instala Tesseract y configura `Ocr:Tesseract` y `Ocr:Tessdata` (ver README).

## 7. Acceso externo (cuando la prueba interna esté bien)

La aplicación está pensada para publicarse con **Cloudflare Tunnel** (`cloudflared`), que entrega HTTPS sin abrir puertos:

1. Cambia el sitio al puerto 80 o ajusta `service:` en el archivo de túnel para que apunte al puerto del sitio.
2. Instala `cloudflared` como servicio con `deploy/cloudflared-config.yml`: `honorarios.<dominio>` (equipo interno,
   detrás de Cloudflare Access) y `boletas.<dominio>` (prestadores, sin Access).
3. En `appsettings.Production.json`, pon `"Publicacion": { "HostPrestadores": "boletas.<dominio>" }`. En ese host solo se
   sirve el portal; el resto de la aplicación solo responde en `honorarios.<dominio>`.

Si no usas Cloudflare, agrega un binding **HTTPS** en IIS con certificado. No expongas el puerto 8080 de prueba a
internet.

## 8. Actualizar a una versión nueva

1. Descarga el artefacto nuevo.
2. Crea `D:\IpsosPagoHonorarios\app\app_offline.htm` (cualquier texto): IIS detiene la aplicación y muestra ese aviso.
3. Copia los archivos nuevos **encima**, sin borrar `appsettings.Production.json` ni la carpeta `logs`. La carpeta
   `archivos` está fuera de `app`, así que no se toca.
4. Elimina `app_offline.htm`. Al iniciar, la aplicación aplica las migraciones que falten.
5. Repite la revisión corta del paso 6.

## 9. Respaldos

Respaldar juntos la base `BD_PagoIpsos` y la carpeta `D:\IpsosPagoHonorarios\archivos`: la base guarda las rutas de los
PDF y comprobantes, y los archivos viven en la carpeta.
