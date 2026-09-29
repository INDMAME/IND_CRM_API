# Secuencia de transcripción de audio con IA

El flujo utiliza OpenAI solo desde el servidor. El navegador nunca recibe las
claves API ni los metadatos originales de OpenAI.

```mermaid
sequenceDiagram
  autonumber
  participant Ui as Interfaz IA React/Razor
  participant Proxy as Proxy MVC o cliente API
  participant Limit as Control de límites de OpenAI
  participant Api as INDSpeechController
  participant Speech as Servicio de transcripción
  participant Mod as Servicio de moderación
  participant OpenAI as OpenAI APIs

  Ui->>Proxy: POST /api/ia/service/speech<br/>multipart audioFile + languageId
  Proxy->>Limit: Reenvía la petición autenticada
  Limit->>Limit: Comprueba la ventana por usuario<br/>y una sola petición de IA activa

  alt Límite de uso o concurrencia superado
    Limit-->>Proxy: 429 IndApiResponse<br/>AI_RATE_LIMIT_EXCEEDED o AI_CONCURRENCY_LIMIT_EXCEEDED
    Proxy-->>Ui: Muestra un mensaje de reintento
  else Petición permitida
    Limit->>Api: Continúa al controlador
    Api->>Api: Valida multipart/form-data
    Api->>Api: Valida languageId, audioFile,<br/>extensión, tipo y máximo de 25 MB
    Api->>Api: Resuelve temperature y prompt/context<br/>o usa el prompt configurado
    Api->>Speech: TranscribeAsync(flujo de audio,<br/>fileName, language, temperature, prompt)
    Speech->>OpenAI: Solicitud de transcripción<br/>Clave de servidor, modelo y response_format json
    OpenAI-->>Speech: JSON con el texto transcrito
    Speech-->>Api: Solo la cadena transcrita
    Api->>Mod: ModerateAsync(transcript)
    Mod->>OpenAI: Solicitud de moderación<br/>Clave de servidor y modelo
    OpenAI-->>Mod: Resultado de la moderación

    alt Transcripción marcada
      Mod-->>Api: flagged=true + categories
      Api-->>Proxy: 422 IndApiResponse<br/>VALIDATION_ERROR
      Proxy-->>Ui: Rechaza la transcripción
    else Transcripción aceptada
      Mod-->>Api: flagged=false
      Api-->>Proxy: 200 IndPagedResponse(string)<br/>Items[0] = transcript + traceId
      Proxy-->>Ui: Texto transcrito
    end
  end
```

## Contratos observados

- Endpoint: `POST /api/ia/service/speech`.
- Entrada: `multipart/form-data`.
- Campos obligatorios: `languageId` y `audioFile`.
- Campos opcionales: `temperature`, `prompt` o `context`.
- Extensiones admitidas por el código: `.mp3`, `.m4a`, `.wav` y `.flac`.
- Tamaño máximo observado: 25 MB.
- Respuesta correcta: `IndPagedResponse<string>` con el texto en `Items`.
- Los errores de validación o dependencia devuelven `IndApiResponse<T>` con
  `traceId`.

## Comportamiento del servicio

El controlador carga el audio en memoria, valida la petición, obtiene la clave
de OpenAI de la configuración del servidor y llama al servicio de transcripción.
El servicio envía el audio y los parámetros del modelo y devuelve solo el
texto.

Después llama a la moderación de OpenAI. Si no está disponible, registra el
problema y permite continuar como contenido no marcado. Si la moderación marca
el texto, el controlador devuelve un error de validación.

## Límites vigentes

- Los audios grandes se cargan en memoria antes de llamar a OpenAI.
- El control de uso protege `speech` por usuario, ventana y concurrencia.
- No se ha verificado en cada pantalla Razor la ruta de interfaz ni el texto de
  reintento que ve la persona usuaria.
- El modelo y el tiempo de espera dependen de la configuración; no deben
  fijarse en la documentación ni en clientes.
