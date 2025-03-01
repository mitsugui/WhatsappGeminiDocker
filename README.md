# Exemplo de Web API em C# com Docker que transfere mensagens enviadas pelo Whatsapp para o Gemini

## Preparação do ambiente

Para o exemplo funcionar é necessário ter uma conta no Whatsapp Business. Siga o [passo a passo](https://developers.facebook.com/docs/whatsapp/cloud-api/get-started) para configurar sua conta e um projeto.

Também é necessário criar um Azure Container Apps e subir o código do exemplo conforme mostrado [aqui](https://learn.microsoft.com/en-us/azure/container-apps/deploy-visual-studio-code)

Este exemplo segue os mesmos princípios explicados nesse [tutorial para AWS](https://developers.facebook.com/docs/whatsapp/cloud-api/guides/set-up-whatsapp-echo-bot). Para saber mais sobre webhooks acesse o [link](https://developers.facebook.com/docs/whatsapp/cloud-api/guides/set-up-webhooks)

No site da Meta para desenvolvedores, abra seus [Apps](https://developers.facebook.com/apps) selecione o app que você criou e na guia "Configuração da API" gere um token "Gerar token de acesso".

Configure um webhook informando a URL da aplicação (por exemplo, https://meuapp.azurecontainerapps.io/Chat) e informe uma palavra chave (ex.: minhachavesecreta) qualquer que será usada para validação. Ative o campo do tipo **messages** (Deve ficar marcado como Assinado)

Configura uma chave de API para acesso ao [Gemini](https://aistudio.google.com/prompts/new_chat) clicando em "Get API Key" e copie a chave gerada.

Configure as seguintes variáveis no appsettings.json ou defina variáveis de ambiente equivalentes:

`Whatsapp:VERIFY_TOKEN: minhachavesecreta` (ou outra palavra chave escolhida)

`Whatsapp:ACCESS_TOKEN: EAA...xxx` (token gerado no portal do desenvolvedor)

`Gemini:API_KEY: AI...xxx` (chave gerada no Gemini)

Existem alguns testes no arquivo Tests.http
