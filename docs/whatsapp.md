# Avisos por WhatsApp

La aplicación puede avisar por WhatsApp a los prestadores cuando deben subir su boleta. Puede enviarlos de tres formas,
que se elige con `WhatsApp:Proveedor`:

- **`InstaPulse`**: a través de la plataforma de integración de IA Ops, que envía por Meta con sus propias pautas y números.
- **`Twilio`**: a través de Twilio, con un número de WhatsApp propio aprobado en Twilio.
- **`Meta`** (valor por defecto): directo con la API de WhatsApp Business (Cloud API de Meta).

Con cualquiera de los tres, WhatsApp solo permite iniciar una conversación con **plantillas** aprobadas, y la
aplicación solo usa esas. Todo lo demás (cuándo se avisa, reintentos, pantalla de seguimiento) funciona igual.

## Cuándo se envía

| Aviso | Cuándo | Plantilla |
| --- | --- | --- |
| Solicitud de boleta | Al importar una planilla de producción: el pago del prestador está listo para boletear | `solicitud_boleta` |
| Recordatorio | Cuando Operaciones o Finanzas usa **Recordar** en Seguimiento de boletas (solo a quien aún no sube su boleta) | `recordatorio_boleta` |
| Boleta observada | Al pedir una nueva boleta o al devolver la planilla con observaciones de boleta | `boleta_observada` |

**Regla del canal:** si el prestador tiene un **celular válido**, el aviso sale por **WhatsApp** (y no por correo). Si no
tiene teléfono (o es un fijo) y tiene correo, sale por **correo**. Si el canal de WhatsApp está desactivado, todos salen
por correo.

El aviso por WhatsApp se envía si se cumplen estas condiciones:

1. El canal está activado (`WhatsApp:Habilitado = true`) y tiene credenciales.
2. Su teléfono es un **celular** válido: chileno (`9 1234 5678`, `+56 9 1234 5678`) o internacional con `+`. Los fijos no tienen WhatsApp.
3. No se envió el mismo aviso (mismos datos) a ese número en las últimas 24 horas. Así, reimportar una planilla o
   repetir **Recordar** no duplica el mensaje (y tampoco lo manda por correo).

La aplicación decide el canal por el teléfono guardado en **Maestros → Prestadores**; no pide una autorización aparte.
Si WhatsApp rechaza después el mensaje (número sin WhatsApp, plantilla no aprobada), el aviso queda como fallido en la
bandeja y **no se reenvía por correo**: se ve en Parámetros y en la tabla `WhatsApp`.

Si Meta no puede entregar el mensaje por una falla transitoria (red, límite de velocidad, error 5xx), la aplicación
reintenta a los 2 y a los 10 minutos (3 intentos en total). Los errores definitivos (plantilla inexistente, número sin
WhatsApp, token inválido) no se reintentan y quedan registrados con el motivo.

## Puesta en marcha con InstaPulse (envío por Meta)

InstaPulse es la plataforma interna que administra los números de WhatsApp de Meta y sus plantillas. La aplicación no
guarda tokens de Meta: solo llama a la API de InstaPulse (`x-remote-user: IAOps`), que usa la **Pauta** configurada allí.

Por cada aviso la aplicación ejecuta los 4 pasos del manual de integración:

1. `POST /participantes` — crea o actualiza al participante (`phone` `+56…`, `name`, `language`, `metadata`).
2. `POST /casos` — abre un caso (`phone_number`, `titulo` = `Pago de Honorarios`).
3. `POST /casos/{caso_id}/sessions` — crea la sesión con la `pauta_id` (`incluir_en_historial_global: false`).
4. `POST /sessions/{session_id}/start/meta` — envía la plantilla (`template_name`, `language_code`, `body_params` en orden, `mark_active`).

**Qué debe entregar el administrador de InstaPulse**

- La **URL base** de la API.
- Una **Pauta** con canal `META` por cada plantilla (o una sola si comparten número): su `PautaID`, el `PHONE_ID`, la
  variable de entorno del `TOKEN` y `TemplatesInicioJSON` con las plantillas aprobadas. Ejemplo para `solicitud_boleta`:

  ```json
  [{ "sid": "solicitud_boleta",
     "text": "Hola {{1}}, tu pago del ciclo {{2}} ya está listo para boletear. Emite una sola boleta de honorarios por {{3}} bruto y súbela en el portal de boletas antes del {{4}}. Gracias.",
     "variables": ["p1", "p2", "p3", "p4"], "header_variables": [] }]
  ```

  `recordatorio_boleta` usa `p1`…`p3` y `boleta_observada` también `p1`…`p3` (textos de la sección de Meta, más abajo).
  El `sid` es el nombre de la plantilla aprobada en Meta.

**Configuración del servidor** (`appsettings.Production.json`, ignorado por git):

```json
"WhatsApp": {
  "Habilitado": true,
  "Proveedor": "InstaPulse",
  "Idioma": "es",
  "InstaPulse": {
    "UrlBase": "https://<host-instapulse>/api",
    "UsuarioRemoto": "IAOps",
    "Pautas": {
      "solicitud_boleta": 101,
      "recordatorio_boleta": 102,
      "boleta_observada": 103
    }
  }
}
```

- Las claves de `Pautas` son los nombres de plantilla de la aplicación; el valor es el `PautaID`. Una plantilla sin
  pauta falla con un error claro y no se reintenta.
- Los parámetros viajan en orden como `body_params` (`p1`, `p2`…) y también quedan en `metadata` del participante.
- `MarcarActiva` (por defecto `true`) corresponde a `mark_active`; `TituloCaso` cambia el título del caso.
- El servidor IIS debe **confiar en el certificado** de InstaPulse y tener salida de red hacia su URL.
- Reiniciar la aplicación y usar **Maestros → Parámetros → Avisos por WhatsApp → Enviar prueba** (debe decir *Proveedor: InstaPulse*).

Errores: cada falla indica el paso (`paso 1 (participantes)` … `paso 4 (start/meta)`) y el detalle que devolvió InstaPulse.
Se reintenta ante red, 429 y 5xx; no se reintenta una respuesta `success:false` ni los códigos permanentes de Meta
(plantilla inexistente, número sin WhatsApp, etc.). Un reintento posterior al paso 3 repite todo el flujo y puede dejar
un caso extra en InstaPulse.

**Consentimiento.** El manual indica que las plantillas UTILITY (avisos de la boleta) no requieren opt-in promocional,
por eso la aplicación no pide una autorización aparte: basta con el teléfono.

## Puesta en marcha con Twilio

Requisitos: una cuenta de Twilio con tu **número de WhatsApp aprobado** (WhatsApp Sender) y plantillas aprobadas.

1. **Credenciales.** En la consola de Twilio, copia el **Account SID** (`AC…`) y el **Auth Token**.
2. **Plantillas.** En Twilio, **Content Template Builder**, crea las tres plantillas de abajo (tipo *Text*, categoría
   **Utility**, idioma Español) con el texto indicado en la sección de Meta. En Twilio las variables se escriben
   `{{1}}`, `{{2}}`… igual que ahí. Envíalas a aprobación de WhatsApp y, cuando estén aprobadas, anota el **Content SID**
   (`HX…`) de cada una.
3. **Configuración del servidor**, en `appsettings.Production.json` (ignorado por git) o variables de entorno.
   **El Auth Token es un secreto: nunca al repositorio.**

   ```json
   "WhatsApp": {
     "Habilitado": true,
     "Proveedor": "Twilio",
     "Twilio": {
       "AccountSid": "<AC…>",
       "AuthToken": "<token>",
       "From": "+56912345678",
       "ContentSids": {
         "solicitud_boleta": "<HX…>",
         "recordatorio_boleta": "<HX…>",
         "boleta_observada": "<HX…>"
       }
     }
   }
   ```

   - `From` es tu número de WhatsApp aprobado en Twilio (con o sin `whatsapp:`). Si prefieres un *Messaging Service*,
     usa `"MessagingServiceSid": "<MG…>"` en lugar de `From`.
   - Las claves de `ContentSids` son los nombres de plantilla de la aplicación (`PlantillaSolicitud`,
     `PlantillaRecordatorio` y `PlantillaObservada`); el valor es el Content SID de Twilio. Una plantilla sin Content
     SID falla con un error claro y no se reintenta.
   - Con variables de entorno: `WhatsApp__Proveedor=Twilio`, `WhatsApp__Twilio__AuthToken=…`,
     `WhatsApp__Twilio__ContentSids__solicitud_boleta=HX…`.
4. **Reiniciar la aplicación** y entrar como Administrador a **Maestros → Parámetros → Avisos por WhatsApp**: debe decir
   *Activado* y *Proveedor: Twilio*. Usa **Enviar prueba** con tu celular. Un `Proveedor` mal escrito detiene el
   arranque con un mensaje que lo indica.

Cómo trabaja con Twilio: la aplicación envía el mensaje y Twilio responde que lo **aceptó** (queda en estado
`queued`). Si WhatsApp lo rechaza después (número sin WhatsApp, plantilla no aprobada, destinatario que bloqueó), ese
fallo no llega a la aplicación: se ve en el **registro de mensajes de la consola de Twilio**. Esa respuesta de Twilio
queda guardada en la bandeja como el identificador del mensaje (`SM…`), para buscarlo allí. Errores frecuentes de
Twilio: `21211` número de destino inválido · `63016` plantilla no aprobada o fuera de la ventana de conversación ·
`63007` el número de origen no está habilitado para WhatsApp · `20003` credenciales inválidas.

## Puesta en marcha con Meta

1. **Cuenta de Meta.** Cuenta de Meta Business (idealmente con el negocio verificado) y una app en
   [Meta for Developers](https://developers.facebook.com) con el producto **WhatsApp**. Asocia un número de teléfono
   de WhatsApp Business. Anota el **Phone number ID** (es un identificador, no el número).
2. **Token permanente.** El token de prueba que muestra Meta vence en 24 horas. Crea un **usuario del sistema** en
   Business Settings, asígnale la app y genera un token con el permiso `whatsapp_business_messaging`.
3. **Plantillas.** En WhatsApp Manager crea las tres plantillas siguientes, categoría **Utilidad**, idioma
   **Español** (si eliges un idioma distinto, como `es_CL`, cámbialo en `WhatsApp:Idioma`). Los nombres deben ser
   exactamente estos, y las variables, en este orden:

   **`solicitud_boleta`**
   ```
   Hola {{1}}, tu pago del ciclo {{2}} ya está listo para boletear. Emite una sola boleta de honorarios por {{3}} bruto y súbela en el portal de boletas antes del {{4}}. Gracias.
   ```
   Variables: 1 nombre · 2 ciclo (OCT-2026) · 3 monto bruto ($331.500) · 4 fecha límite (10-11-2026).

   **`recordatorio_boleta`**
   ```
   Hola {{1}}, aún no recibimos tu boleta de honorarios del ciclo {{2}}. Súbela en el portal de boletas antes del {{3}} para que podamos pagarte a tiempo. Gracias.
   ```
   Variables: 1 nombre · 2 ciclo · 3 fecha límite.

   **`boleta_observada`**
   ```
   Hola {{1}}, revisamos tu boleta y tiene una observación: {{2}}. Por favor sube una nueva boleta en el portal de boletas {{3}}. Gracias.
   ```
   Variables: 1 nombre · 2 motivo de la observación · 3 plazo (“antes de las 22:52” o “a la brevedad”).

   Conviene agregar al texto la dirección del portal de prestadores (por ejemplo, "Ingresa en https://boletas.&lt;dominio&gt;")
   o un botón con ese enlace. Al crear cada plantilla, Meta pide un ejemplo para cada variable. Las aprobaciones
   suelen tardar de minutos a un par de días.
4. **Configuración del servidor.** En `appsettings.Production.json` (ignorado por git) o en variables de entorno
   (`WhatsApp__AccessToken`, etc.). **El token es un secreto: nunca al repositorio.** Ejemplo en
   `deploy/appsettings.Production.ejemplo.json`:

   ```json
   "WhatsApp": { "Habilitado": true, "PhoneNumberId": "<id>", "AccessToken": "<token>", "Idioma": "es" }
   ```

   Otras opciones: `ApiVersion` (por defecto `v21.0`), `MaxIntentos` (3) y los nombres de plantilla
   `PlantillaSolicitud`, `PlantillaRecordatorio` y `PlantillaObservada`, si usas otros nombres.
5. **Reiniciar la aplicación** y entrar como Administrador a **Maestros → Parámetros → Avisos por WhatsApp**. Debe decir
   *Activado*. Usa **Enviar prueba** con tu celular: envía la plantilla `solicitud_boleta` con datos ficticios y
   muestra el resultado o el error de Meta. Hazlo antes de pedir a los prestadores que autoricen.
6. **Base de datos.** La aplicación crea la tabla `WhatsApp` al iniciar si tiene permiso
   `db_ddladmin`. Si no, un DBA ejecuta `database/09_WhatsApp.sql`.

## Seguimiento

**Maestros → Parámetros → Avisos por WhatsApp** muestra los mensajes en cola, los enviados en las últimas 24 horas, los
fallidos y el último error. Cada mensaje queda en la tabla `WhatsApp` con su estado, intentos, error y el identificador
del mensaje en WhatsApp.

Errores frecuentes de Meta: `132001` la plantilla no existe con ese nombre o idioma · `132000` el número de variables
no coincide · `131026` el número no tiene WhatsApp · `190` el token venció o es inválido.

## Consideraciones

- **Costo.** Se paga por mensaje según la categoría de la plantilla: la tarifa de Meta y, con Twilio, además la de
  Twilio por mensaje. Revisa las tarifas para Chile antes de activar el canal.
- **Límites.** Meta limita cuántos destinatarios distintos puede recibir un número por día, y el límite parte bajo y
  sube con el uso y la verificación del negocio. Un día de punta con muchos prestadores puede superarlo: los mensajes
  excedidos fallan con error, quedan registrados y los prestadores igual reciben el correo.
- **Calidad del número.** Si los prestadores bloquean o reportan los mensajes, Meta puede reducir el límite del número.
  Por eso solo se envían avisos de la boleta.
- **Pruebas.** El envío, con ambos proveedores, se probó con pruebas automáticas y contra servidores que imitan las API
  de Meta y de Twilio (formato de la solicitud, autenticación, reintentos y errores). La primera conexión real se
  valida con **Enviar prueba**.
