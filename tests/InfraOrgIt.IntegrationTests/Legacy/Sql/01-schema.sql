-- Schema legacy ridotto per il rehearsal della Fase 7.
-- Ricostruito dai mapping in AllInfraOrgIT/docs/migration/legacy-mapping.md:
-- contiene solo le colonne lette da tools/LegacyImport. Nessun dato o artefatto reale.
-- Il database è già selezionato da MYSQL_DATABASE / connection string.

CREATE TABLE `Environments` (
  `Id` INT NOT NULL PRIMARY KEY,
  `Name` VARCHAR(200) NOT NULL,
  `Description` TEXT NULL
);

CREATE TABLE `Providers` (
  `Id` INT NOT NULL PRIMARY KEY,
  `Name` VARCHAR(200) NOT NULL
);

CREATE TABLE `ProviderEnvironments` (
  `Id` INT NOT NULL PRIMARY KEY,
  `ProviderId` INT NOT NULL,
  `EnvironmentId` INT NOT NULL
);

CREATE TABLE `Roles` (
  `Id` INT NOT NULL PRIMARY KEY,
  `Name` VARCHAR(200) NOT NULL,
  `Label` VARCHAR(200) NOT NULL,
  `ScopeEnum` INT NOT NULL
);

CREATE TABLE `Menus` (
  `Id` INT NOT NULL PRIMARY KEY,
  `Name` VARCHAR(200) NOT NULL,
  `Path` VARCHAR(200) NOT NULL,
  `IsActive` TINYINT(1) NOT NULL,
  `HideBackButton` TINYINT(1) NOT NULL,
  `IsVisible` TINYINT(1) NOT NULL,
  `Order` INT NOT NULL,
  `ParentId` INT NULL
);

CREATE TABLE `MenuRole` (
  `MenuId` INT NOT NULL,
  `RolesId` INT NOT NULL,
  PRIMARY KEY (`MenuId`, `RolesId`)
);

CREATE TABLE `Users` (
  `Id` INT NOT NULL PRIMARY KEY,
  `Name` VARCHAR(200) NOT NULL,
  `Surname` VARCHAR(200) NOT NULL,
  `Username` VARCHAR(200) NOT NULL
);

CREATE TABLE `Groups` (
  `Id` INT NOT NULL PRIMARY KEY,
  `Name` VARCHAR(200) NOT NULL
);

CREATE TABLE `GroupUser` (
  `GroupsId` INT NOT NULL,
  `UsersId` INT NOT NULL,
  PRIMARY KEY (`GroupsId`, `UsersId`)
);

CREATE TABLE `RoleUser` (
  `RolesId` INT NOT NULL,
  `UsersId` INT NOT NULL,
  PRIMARY KEY (`RolesId`, `UsersId`)
);

CREATE TABLE `ProviderEnvironmentGroupRoles` (
  `Id` INT NOT NULL PRIMARY KEY,
  `ProviderEnvironmentId` INT NOT NULL,
  `GroupId` INT NOT NULL,
  `RoleId` INT NOT NULL
);

CREATE TABLE `Applications` (
  `Id` INT NOT NULL PRIMARY KEY,
  `Name` VARCHAR(200) NOT NULL,
  `Description` TEXT NULL,
  `BasePath` VARCHAR(50) NOT NULL,
  `ListeningOnPort` INT NOT NULL
);

CREATE TABLE `ApplicationVersions` (
  `Id` INT NOT NULL PRIMARY KEY,
  `ApplicationId` INT NOT NULL,
  `VersionNumber` VARCHAR(50) NOT NULL
);

CREATE TABLE `ApplicationVersionApis` (
  `Id` INT NOT NULL PRIMARY KEY,
  `ApplicationVersionId` INT NOT NULL,
  `Url` VARCHAR(200) NOT NULL,
  `RequestTypeEnum` INT NOT NULL,
  `Description` TEXT NULL,
  `Body` LONGTEXT NULL
);

CREATE TABLE `ApplicationApiQueryParams` (
  `Id` INT NOT NULL PRIMARY KEY,
  `ApplicationApiId` INT NOT NULL,
  `Name` VARCHAR(200) NOT NULL,
  `ParamType` INT NOT NULL
);

CREATE TABLE `Projects` (
  `Id` INT NOT NULL PRIMARY KEY,
  `Name` VARCHAR(200) NOT NULL,
  `Description` TEXT NULL
);

CREATE TABLE `ApplicationProject` (
  `ApplicationsId` INT NOT NULL,
  `ProjectsId` INT NOT NULL,
  PRIMARY KEY (`ApplicationsId`, `ProjectsId`)
);

CREATE TABLE `CommandTemplates` (
  `Id` INT NOT NULL PRIMARY KEY,
  `Name` VARCHAR(200) NOT NULL,
  `Description` TEXT NULL,
  `Script` LONGTEXT NOT NULL
);

CREATE TABLE `ApplicationActions` (
  `Id` INT NOT NULL PRIMARY KEY,
  `ApplicationId` INT NOT NULL,
  `Label` VARCHAR(100) NOT NULL,
  `Order` INT NOT NULL,
  `CommandTemplateId` INT NULL,
  `CustomScript` LONGTEXT NULL,
  `ParameterValues` LONGTEXT NOT NULL,
  `RequiresConfirmation` TINYINT(1) NOT NULL
);

CREATE TABLE `ServerCredentials` (
  `Id` INT NOT NULL PRIMARY KEY,
  `Name` VARCHAR(200) NOT NULL,
  `Username` VARCHAR(200) NOT NULL,
  `AuthType` VARCHAR(20) NOT NULL,
  `EncryptedSecret` LONGTEXT NOT NULL,
  `EncryptedPassphrase` LONGTEXT NULL
);

CREATE TABLE `Servers` (
  `Id` INT NOT NULL PRIMARY KEY,
  `ProviderEnvironmentId` INT NOT NULL,
  `Name` VARCHAR(200) NOT NULL,
  `IpAddress` VARCHAR(100) NULL,
  `Hostname` VARCHAR(200) NULL,
  `CredentialId` INT NULL,
  `SshPort` INT NOT NULL,
  `HostKeyFingerprint` VARCHAR(200) NULL
);

CREATE TABLE `ProxyConnections` (
  `Id` INT NOT NULL PRIMARY KEY,
  `SourceServerId` INT NOT NULL,
  `TargetServerId` INT NOT NULL,
  `Direction` VARCHAR(20) NOT NULL,
  `CreatedAt` DATETIME(6) NOT NULL
);

CREATE TABLE `ServerApplications` (
  `Id` INT NOT NULL PRIMARY KEY,
  `ServerId` INT NOT NULL,
  `ApplicationVersionId` INT NOT NULL,
  `ExposedPort` INT NOT NULL,
  `ProxyRouteId` INT NULL
);

CREATE TABLE `ProxyRoutes` (
  `Id` INT NOT NULL PRIMARY KEY,
  `ProxyConnectionId` INT NOT NULL,
  `Route` VARCHAR(200) NOT NULL,
  `MappedTo` VARCHAR(50) NULL,
  `TargetServerApplicationId` INT NULL,
  `TargetProxyConnectionId` INT NULL
);

CREATE TABLE `ReachabilityTests` (
  `Id` INT NOT NULL PRIMARY KEY,
  `ServerId` INT NOT NULL,
  `Hostname` VARCHAR(200) NOT NULL,
  `Port` INT NOT NULL,
  `Description` VARCHAR(200) NULL
);

CREATE TABLE `Diagrams` (
  `Id` INT NOT NULL PRIMARY KEY,
  `Name` VARCHAR(200) NOT NULL,
  `Description` TEXT NULL,
  `IsDefault` TINYINT(1) NOT NULL,
  `CreatedAt` DATETIME(6) NOT NULL
);

CREATE TABLE `DiagramScopeItems` (
  `Id` INT NOT NULL PRIMARY KEY,
  `DiagramId` INT NOT NULL,
  `ScopeType` VARCHAR(30) NOT NULL,
  `EntityId` INT NOT NULL
);

CREATE TABLE `DiagramNodes` (
  `Id` INT NOT NULL PRIMARY KEY,
  `DiagramId` INT NOT NULL,
  `NodeType` VARCHAR(30) NOT NULL,
  `EntityId` INT NOT NULL,
  `X` DOUBLE NOT NULL,
  `Y` DOUBLE NOT NULL,
  `Width` DOUBLE NULL,
  `Height` DOUBLE NULL,
  `Collapsed` TINYINT(1) NOT NULL
);

CREATE TABLE `DiagramEdges` (
  `Id` INT NOT NULL PRIMARY KEY,
  `DiagramId` INT NOT NULL,
  `ProxyConnectionId` INT NOT NULL,
  `TargetNodeType` VARCHAR(30) NOT NULL,
  `TargetEntityId` INT NOT NULL,
  `SourceHandle` VARCHAR(50) NULL,
  `TargetHandle` VARCHAR(50) NULL,
  `BendX` DOUBLE NULL,
  `BendY` DOUBLE NULL
);

CREATE TABLE `CommandExecutions` (
  `Id` INT NOT NULL PRIMARY KEY,
  `Username` VARCHAR(100) NOT NULL,
  `ServerId` INT NOT NULL,
  `ServerName` VARCHAR(200) NOT NULL,
  `Kind` VARCHAR(20) NOT NULL,
  `Label` VARCHAR(200) NOT NULL,
  `Script` LONGTEXT NOT NULL,
  `ExitCode` INT NULL,
  `Output` LONGTEXT NOT NULL,
  `StartedAt` DATETIME(6) NOT NULL,
  `EndedAt` DATETIME(6) NULL
);
