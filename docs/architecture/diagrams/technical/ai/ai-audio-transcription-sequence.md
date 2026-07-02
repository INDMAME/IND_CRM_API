# AI audio transcription sequence

This diagram documents the audio transcription communication path. The flow
uses OpenAI from the server side only; the browser never receives API keys or
OpenAI raw metadata.

```mermaid
sequenceDiagram
  autonumber
  participant Ui as React/Razor AI UI
  participant Proxy as MVC proxy or API client
  participant Limit as OpenAI rate limit handler
  participant Api as INDSpeechController
  participant Speech as Audio transcription service
  participant Mod as Text moderation service
  participant OpenAI as OpenAI APIs

  Ui->>Proxy: POST /api/ia/service/speech<br/>multipart audioFile + languageId
  Proxy->>Limit: Forward authenticated request
  Limit->>Limit: Check per-user rate window<br/>and one active AI request

  alt Rate or concurrency limit exceeded
    Limit-->>Proxy: 429 IndApiResponse<br/>AI_RATE_LIMIT_EXCEEDED or AI_CONCURRENCY_LIMIT_EXCEEDED
    Proxy-->>Ui: Surface retry message
  else Request allowed
    Limit->>Api: Continue to controller
    Api->>Api: Validate multipart/form-data
    Api->>Api: Validate languageId, audioFile,<br/>extension, content type, 25 MB max
    Api->>Api: Resolve temperature and prompt/context<br/>or configured default prompt
    Api->>Speech: TranscribeAsync(audio stream,<br/>fileName, language, temperature, prompt)
    Speech->>OpenAI: Audio transcription request<br/>server-side key, model, response_format json
    OpenAI-->>Speech: JSON with transcript text
    Speech-->>Api: Transcript string only
    Api->>Mod: ModerateAsync(transcript)
    Mod->>OpenAI: Moderation request<br/>server-side key and moderation model
    OpenAI-->>Mod: Moderation result

    alt Transcript flagged
      Mod-->>Api: flagged=true + categories
      Api-->>Proxy: 422 IndApiResponse<br/>VALIDATION_ERROR
      Proxy-->>Ui: Reject transcript
    else Transcript accepted
      Mod-->>Api: flagged=false
      Api-->>Proxy: 200 IndPagedResponse(string)<br/>Items[0] = transcript + traceId
      Proxy-->>Ui: Transcript text
    end
  end
```

## Observed contracts

- Endpoint: `POST /api/ia/service/speech`.
- Input: `multipart/form-data`.
- Required fields: `languageId`, `audioFile`.
- Optional fields: `temperature`, `prompt` or `context`.
- Allowed audio extensions observed in code: `.mp3`, `.m4a`, `.wav`,
  `.flac`.
- Maximum audio size observed in code: 25 MB.
- Success envelope: `IndPagedResponse<string>` with transcript text in
  `Items`.
- Validation or dependency errors return `IndApiResponse<T>` with `traceId`.

## Service behavior

The controller reads the audio into memory, validates the request, obtains the
OpenAI key from server configuration, and calls the audio transcription
service. The service sends the audio and model parameters to OpenAI and
returns only the text field.

After transcription, the controller calls OpenAI moderation. If moderation is
unavailable, the moderation service logs the issue and returns non-flagged, so
the transcription flow can continue. If moderation flags the text, the
controller returns a validation error.

## Risks and pending validation

- Large audio files are read into memory before calling OpenAI.
- The rate-limit handler protects `speech` by user, request window, and
  concurrency.
- Exact UI route and user-facing retry text are pendiente de validar for every
  Razor-only screen.
- The OpenAI model and timeout are configuration-driven and should not be
  hardcoded in docs or clients.
