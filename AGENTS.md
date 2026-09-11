# Entrega de desarrollo

Preferencia explícita del usuario, 11 de septiembre de 2026:

- Los candidatos gráficos deben poder activarse y desactivarse desde las opciones experimentales del mod, con el estado inicial apagado.
- Resultado final de la prueba: el usuario prefiere usar NeuralFX con las capacidades experimentales desactivadas porque el defecto se nota mucho menos y la imagen se estabiliza antes. Conservar esa elección; no reactivarlas ni continuar investigando el terreno salvo nueva petición. El candidato de profundidad no corrigió el defecto.
- Al terminar desarrollo, compilar, verificar y desplegar siempre la última versión del mod, Hub, puente y shaders.
- El usuario autoriza cerrar Cities: Skylines y el Hub para desplegar. Pedir el cierre normal y comprobar que ambos procesos terminaron antes de modificar la instalación.
- Ejecutar una desinstalación y reinstalación completa del pipeline usando los servicios transaccionales del Hub, con copias de seguridad y conservación de los ajustes. No basta con copiar una DLL ni con dejar un candidato fuera del juego.
- `tools/package.ps1 -Deploy` es el camino de entrega. Verificar después los hashes del mod, Hub, addon y shader instalados y el manifiesto del juego.
- Las pruebas visuales en ciudad las hace el usuario. La autorización de cierre/despliegue no implica abrir o manejar la partida para probar gráficos.

Esta instrucción de entrega sustituye las notas históricas de CONTINUAR.md que pedían no cerrar el juego o dejar el pipeline sin instalar.
