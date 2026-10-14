# Flujo funcional: transcripción de audio

Fuente técnica:
[ai-audio-transcription-sequence.md](../../technical/ai/ai-audio-transcription-sequence.md)

Esta versión explica las mismas decisiones y resultados con lenguaje sencillo.

```mermaid
sequenceDiagram
  autonumber
  participant User as Usuario
  participant Screen as Pantalla de audio
  participant System as Sistema CRM<br/>(aplicación que coordina)
  participant Limit as Control de uso<br/>(evita exceso de IA)
  participant Voice as IA de voz<br/>(convierte audio en texto)
  participant Review as Revisión de contenido<br/>(comprueba si el texto es aceptable)
  participant OpenAI as OpenAI<br/>(servicio externo de IA)

  User->>Screen: Selecciona audio e idioma
  Screen->>System: Envía el audio para transcribir
  System->>Limit: Comprueba el límite de uso<br/>y si ya hay otra IA en curso

  alt Se supera el límite de uso
    Limit-->>System: Bloquea temporalmente la solicitud
    System-->>Screen: Mensaje para intentarlo más tarde
    Screen-->>User: Muestra un aviso de límite
  else Solicitud permitida
    Limit->>System: Permite continuar
    System->>System: Comprueba formato, tamaño<br/>e idioma del audio
    System->>System: Prepara instrucciones opcionales<br/>(contexto para mejorar el texto)
    System->>Voice: Pide convertir audio a texto
    Voice->>OpenAI: Envía audio al servicio de IA
    OpenAI-->>Voice: Devuelve texto transcrito
    Voice-->>System: Entrega solo el texto
    System->>Review: Revisa el texto generado
    Review->>OpenAI: Pide moderación<br/>(revisión automática de contenido)
    OpenAI-->>Review: Resultado de la revisión

    alt Texto rechazado
      Review-->>System: Contenido no aceptado
      System-->>Screen: Mensaje de rechazo
      Screen-->>User: Informa que no se puede usar el texto
    else Texto aceptado
      Review-->>System: Contenido aceptado
      System-->>Screen: Texto transcrito<br/>y referencia de seguimiento
      Screen-->>User: Muestra la transcripción
    end
  end
```

## Explicación funcional

La persona envía un audio y recibe texto. El sistema comprueba primero los
límites de uso y las reglas del archivo. Después, una IA convierte el audio en
texto y otra revisión automática comprueba si puede mostrarse.
