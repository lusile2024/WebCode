-- 统一工作区管理架构数据库迁移脚本
-- 执行顺序：先备份数据库，然后执行此脚本

-- 1. 创建工作区目录表
CREATE TABLE IF NOT EXISTS `WorkspaceDirectory` (
  `DirectoryId` bigint NOT NULL AUTO_INCREMENT COMMENT '目录ID',
  `DirectoryPath` varchar(1024) NOT NULL COMMENT '目录路径',
  `DirectoryName` varchar(255) DEFAULT NULL COMMENT '目录名称',
  `DirectoryType` varchar(32) NOT NULL DEFAULT 'Default' COMMENT '目录类型：Default/Custom/Project/Shared',
  `OwnerUsername` varchar(128) NOT NULL COMMENT '所有者用户名',
  `Description` varchar(512) DEFAULT NULL COMMENT '目录描述',
  `IsValid` tinyint(1) NOT NULL DEFAULT '1' COMMENT '是否有效',
  `CreatedAt` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  `UpdatedAt` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新时间',
  PRIMARY KEY (`DirectoryId`),
  UNIQUE KEY `uk_owner_path` (`OwnerUsername`, `DirectoryPath`(255)),
  KEY `idx_owner` (`OwnerUsername`),
  KEY `idx_type` (`DirectoryType`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='工作区目录表';

-- 2. 创建目录授权表
CREATE TABLE IF NOT EXISTS `WorkspaceAuthorization` (
  `AuthorizationId` bigint NOT NULL AUTO_INCREMENT COMMENT '授权ID',
  `DirectoryId` bigint NOT NULL COMMENT '目录ID',
  `AuthorizedUsername` varchar(128) NOT NULL COMMENT '被授权用户名',
  `GrantorUsername` varchar(128) NOT NULL COMMENT '授权人用户名',
  `PermissionLevel` varchar(32) NOT NULL DEFAULT 'ReadOnly' COMMENT '权限级别：ReadOnly/ReadWrite/Owner',
  `ExpiresAt` datetime DEFAULT NULL COMMENT '过期时间，null表示永不过期',
  `CreatedAt` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  `UpdatedAt` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新时间',
  PRIMARY KEY (`AuthorizationId`),
  UNIQUE KEY `uk_directory_user` (`DirectoryId`,`AuthorizedUsername`),
  KEY `idx_authorized_user` (`AuthorizedUsername`),
  KEY `idx_directory` (`DirectoryId`),
  CONSTRAINT `fk_auth_directory` FOREIGN KEY (`DirectoryId`) REFERENCES `WorkspaceDirectory` (`DirectoryId`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='目录授权表';

-- 3. 创建会话目录映射表
CREATE TABLE IF NOT EXISTS `SessionWorkspaceMapping` (
  `MappingId` bigint NOT NULL AUTO_INCREMENT COMMENT '映射ID',
  `SessionId` varchar(128) NOT NULL COMMENT '会话ID',
  `DirectoryId` bigint NOT NULL COMMENT '目录ID',
  `Username` varchar(128) NOT NULL COMMENT '用户名',
  `IsActive` tinyint(1) NOT NULL DEFAULT '1' COMMENT '是否是当前活跃目录',
  `CreatedAt` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  `UpdatedAt` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新时间',
  PRIMARY KEY (`MappingId`),
  UNIQUE KEY `uk_session_directory` (`SessionId`,`DirectoryId`),
  KEY `idx_session` (`SessionId`),
  KEY `idx_directory` (`DirectoryId`),
  KEY `idx_username` (`Username`),
  CONSTRAINT `fk_mapping_directory` FOREIGN KEY (`DirectoryId`) REFERENCES `WorkspaceDirectory` (`DirectoryId`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='会话目录映射表';

-- 4. 扩展ChatSession表，添加目录相关字段
ALTER TABLE `ChatSession`
ADD COLUMN IF NOT EXISTS `DirectoryId` bigint DEFAULT NULL COMMENT '关联的目录ID',
ADD COLUMN IF NOT EXISTS `DirectoryName` varchar(255) DEFAULT NULL COMMENT '目录名称（冗余存储，用于快速显示）',
ADD COLUMN IF NOT EXISTS `DirectoryType` varchar(32) DEFAULT NULL COMMENT '目录类型（冗余存储）',
ADD COLUMN IF NOT EXISTS `DirectoryOwner` varchar(128) DEFAULT NULL COMMENT '目录所有者（冗余存储）',
ADD KEY IF NOT EXISTS `idx_directory` (`DirectoryId`);

-- 5. 插入初始数据（可选）
-- INSERT INTO WorkspaceDirectory (DirectoryPath, DirectoryName, DirectoryType, OwnerUsername)
-- VALUES ('/default/workspace', '默认工作区', 'Default', 'default');
