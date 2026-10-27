-- Dati fittizi per il rehearsal Fase 7: una macchina, un proxy, un servizio,
-- un diagramma e una esecuzione. ID alti per non collidere con i seed del target.
-- La credenziale ha segreto vuoto: l'importer la marca NeedsReauth.

INSERT INTO `Environments` (`Id`, `Name`, `Description`) VALUES
  (7101, 'Rehearsal', 'Ambiente fittizio per il rehearsal Fase 7');

INSERT INTO `Providers` (`Id`, `Name`) VALUES
  (7201, 'Provider Rehearsal');

INSERT INTO `ProviderEnvironments` (`Id`, `ProviderId`, `EnvironmentId`) VALUES
  (7301, 7201, 7101);

INSERT INTO `Roles` (`Id`, `Name`, `Label`, `ScopeEnum`) VALUES
  (7401, 'RehearsalViewer', 'Rehearsal viewer', 1);

INSERT INTO `Menus` (`Id`, `Name`, `Path`, `IsActive`, `HideBackButton`, `IsVisible`, `Order`, `ParentId`) VALUES
  (7501, 'Rehearsal', '/rehearsal', 1, 1, 1, 10, NULL),
  (7502, 'Rehearsal server', '/rehearsal/servers', 1, 0, 1, 1, 7501);

INSERT INTO `MenuRole` (`MenuId`, `RolesId`) VALUES
  (7501, 7401),
  (7502, 7401);

INSERT INTO `Users` (`Id`, `Name`, `Surname`, `Username`) VALUES
  (7601, 'Mario', 'Rossi', 'mario.rossi');

INSERT INTO `Groups` (`Id`, `Name`) VALUES
  (7651, 'Rehearsal group');

INSERT INTO `GroupUser` (`GroupsId`, `UsersId`) VALUES
  (7651, 7601);

INSERT INTO `RoleUser` (`RolesId`, `UsersId`) VALUES
  (7401, 7601);

INSERT INTO `ProviderEnvironmentGroupRoles` (`Id`, `ProviderEnvironmentId`, `GroupId`, `RoleId`) VALUES
  (7701, 7301, 7651, 7401);

INSERT INTO `Applications` (`Id`, `Name`, `Description`, `BasePath`, `ListeningOnPort`) VALUES
  (7801, 'Servizio Rehearsal', 'Servizio fittizio', '/servizio', 8081);

INSERT INTO `ApplicationVersions` (`Id`, `ApplicationId`, `VersionNumber`) VALUES
  (7811, 7801, '1.0.0');

INSERT INTO `ApplicationVersionApis` (`Id`, `ApplicationVersionId`, `Url`, `RequestTypeEnum`, `Description`, `Body`) VALUES
  (7821, 7811, '/health', 0, 'Health check fittizio', '');

INSERT INTO `ApplicationApiQueryParams` (`Id`, `ApplicationApiId`, `Name`, `ParamType`) VALUES
  (7831, 7821, 'verbose', 2);

INSERT INTO `Projects` (`Id`, `Name`, `Description`) VALUES
  (7841, 'Progetto Rehearsal', 'Progetto fittizio');

INSERT INTO `ApplicationProject` (`ApplicationsId`, `ProjectsId`) VALUES
  (7801, 7841);

INSERT INTO `CommandTemplates` (`Id`, `Name`, `Description`, `Script`) VALUES
  (7851, 'Riavvio rehearsal', 'Riavvio del servizio fittizio', 'systemctl restart servizio');

INSERT INTO `ApplicationActions` (`Id`, `ApplicationId`, `Label`, `Order`, `CommandTemplateId`, `CustomScript`, `ParameterValues`, `RequiresConfirmation`) VALUES
  (7861, 7801, 'Riavvia', 1, 7851, NULL, '{"service":"servizio"}', 1);

INSERT INTO `ServerCredentials` (`Id`, `Name`, `Username`, `AuthType`, `EncryptedSecret`, `EncryptedPassphrase`) VALUES
  (7901, 'Credenziale rehearsal', 'rehearsal', 'Password', '', NULL);

INSERT INTO `Servers` (`Id`, `ProviderEnvironmentId`, `Name`, `IpAddress`, `Hostname`, `CredentialId`, `SshPort`, `HostKeyFingerprint`) VALUES
  (7911, 7301, 'Server rehearsal', '10.10.10.10', 'server-rehearsal', 7901, 22, NULL),
  (7912, 7301, 'Proxy rehearsal', '10.10.10.11', 'proxy-rehearsal', 7901, 22, NULL);

INSERT INTO `ProxyConnections` (`Id`, `SourceServerId`, `TargetServerId`, `Direction`, `CreatedAt`) VALUES
  (7921, 7911, 7912, 'Outgoing', '2026-10-20 10:00:00.000000');

INSERT INTO `ServerApplications` (`Id`, `ServerId`, `ApplicationVersionId`, `ExposedPort`, `ProxyRouteId`) VALUES
  (7931, 7911, 7811, 8081, 7941);

INSERT INTO `ProxyRoutes` (`Id`, `ProxyConnectionId`, `Route`, `MappedTo`, `TargetServerApplicationId`, `TargetProxyConnectionId`) VALUES
  (7941, 7921, '/servizio', 'servizio-fittizio', NULL, NULL);

INSERT INTO `ReachabilityTests` (`Id`, `ServerId`, `Hostname`, `Port`, `Description`) VALUES
  (7951, 7911, '10.10.10.10', 8081, 'Servizio fittizio');

INSERT INTO `Diagrams` (`Id`, `Name`, `Description`, `IsDefault`, `CreatedAt`) VALUES
  (7961, 'Diagramma rehearsal', 'Diagramma fittizio', 1, '2026-10-20 10:05:00.000000');

INSERT INTO `DiagramScopeItems` (`Id`, `DiagramId`, `ScopeType`, `EntityId`) VALUES
  (7971, 7961, 'ProviderEnvironment', 7101);

INSERT INTO `DiagramNodes` (`Id`, `DiagramId`, `NodeType`, `EntityId`, `X`, `Y`, `Width`, `Height`, `Collapsed`) VALUES
  (7981, 7961, 'Server', 7911, 100, 100, 220, 120, 0),
  (7982, 7961, 'Server', 7912, 400, 100, 220, 120, 0);

INSERT INTO `DiagramEdges` (`Id`, `DiagramId`, `ProxyConnectionId`, `TargetNodeType`, `TargetEntityId`, `SourceHandle`, `TargetHandle`, `BendX`, `BendY`) VALUES
  (7991, 7961, 7921, 'Server', 7912, 'right', 'left', 250, 0);

INSERT INTO `CommandExecutions` (`Id`, `Username`, `ServerId`, `ServerName`, `Kind`, `Label`, `Script`, `ExitCode`, `Output`, `StartedAt`, `EndedAt`) VALUES
  (7999, 'mario.rossi', 7911, 'Server rehearsal', 'Action', 'Riavvio', 'systemctl restart servizio', 0, 'output fittizio con dettagli', '2026-10-20 11:00:00.000000', '2026-10-20 11:00:05.000000');
