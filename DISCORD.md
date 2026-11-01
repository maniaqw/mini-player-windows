# Discord Rich Presence

Откройте `⋯ → Настройки плеера → Discord Rich Presence`. Интеграция включена по умолчанию с предоставленным Application ID `1566480128328990790`. Другой публичный Application ID можно указать в этом же разделе. Токен бота, Client Secret и вход в аккаунт внутри плеера не нужны.

Когда музыка играет, Discord получает:

- название трека;
- музыкальное приложение и исполнителя, например `Apple Music · Artist`;
- временные отметки для прогресса трека.

На паузе, при отсутствии медиасессии, отключении интеграции и закрытии плеера активность очищается. После перезапуска Discord подключение восстанавливается автоматически. Нужен настольный Discord, запущенный на том же компьютере. Видимость активности также зависит от настроек активности и приватности самого Discord. Демо-режим плеера не публикуется.

Заголовок приложения в профиле берётся из имени приложения в Developer Portal; музыкальный источник отображается в строке активности. Локальные обложки не загружаются на внешние сервисы.

Соединение использует локальные именованные каналы Discord IPC. Команды последовательно подтверждаются ответами Discord, изменения объединяются с интервалом до 5 секунд; очистка статуса выполняется сразу. В настройках отображается состояние подключения, а сообщение отказа Discord доступно в подсказке к состоянию.

Установщик версии 2.3.0: `release/GlassPlayer-Setup-2.3.0.exe`. Настройки сохраняются при обновлении.

English: Open `⋯ → Player settings → Discord Rich Presence`. Enable or disable sharing and change the public Application ID there. Your song, artist, music app and progress are shown while playing. Pausing or closing the player clears the activity. The desktop Discord app must be running. No bot token or client secret is required.

Protocol references: [Discord RPC](https://discord.com/developers/docs/topics/rpc), [Discord RPC implementation notes](https://github.com/discord/discord-rpc/blob/master/documentation/hard-mode.md).
