# Regla de versionado Postman

Cuando el usuario pida "genera un archivo postman" o solicite crear una nueva version del proyecto:
- Mantener dos lineas separadas: `.codex/Postman/DEV` y `.codex/Postman/PROD`.
- `PROD` conserva el historico y las snapshots productivas sin sobrescribir versiones anteriores.
- `DEV` contiene la linea activa de trabajo y arranca en `V01` tras la separacion de entornos.
- Cuando se cree una nueva version `DEV`, usa siempre la ultima coleccion de `.codex/Postman/DEV` como base e incrementa la version dentro de esa carpeta.
- Si no existe aun una coleccion en `DEV`, usa la ultima de `PROD` como base de arranque.
- Cuando una coleccion de `DEV` se promocione a `PROD`, copiarla a `.codex/Postman/PROD`, ajustar `baseUrl` a produccion si aplica y versionar sin sobrescribir el historico.
- Mantener la copia de soporte sincronizada en `Notes/DEV` o `Notes/PROD`.
- Actualiza el nombre de la collection y el `_postman_id`.
