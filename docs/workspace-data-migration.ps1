<#
.SYNOPSIS
工作区数据迁移脚本
将现有会话的工作区数据迁移到新的统一工作区管理架构
#>

param(
    [string]$ConnectionString = "server=localhost;database=webcode;uid=root;pwd=123456;",
    [switch]$DryRun = $false
)

Write-Host "=== 统一工作区数据迁移工具 ===" -ForegroundColor Cyan
Write-Host "连接字符串: $ConnectionString"
Write-Host "是否模拟执行: $DryRun"
Write-Host ""

# 加载MySQL连接器
try {
    Add-Type -Path "MySql.Data.dll" -ErrorAction Stop
}
catch {
    Write-Host "错误：无法加载MySQL.Data.dll，请确保该文件在当前目录下" -ForegroundColor Red
    exit 1
}

$connection = New-Object MySql.Data.MySqlClient.MySqlConnection
$connection.ConnectionString = $ConnectionString

try {
    $connection.Open()
    Write-Host "✅ 数据库连接成功" -ForegroundColor Green
}
catch {
    Write-Host "❌ 数据库连接失败: $_" -ForegroundColor Red
    exit 1
}

try {
    # 1. 获取所有有工作区路径的会话
    $query = "SELECT SessionId, WorkspacePath, Username, CreatedAt FROM ChatSession WHERE WorkspacePath IS NOT NULL AND WorkspacePath != ''"
    $command = New-Object MySql.Data.MySqlClient.MySqlCommand($query, $connection)
    $reader = $command.ExecuteReader()

    $sessions = @()
    while ($reader.Read()) {
        $sessions += [PSCustomObject]@{
            SessionId = $reader.GetString("SessionId")
            WorkspacePath = $reader.GetString("WorkspacePath")
            Username = $reader.GetString("Username")
            CreatedAt = $reader.GetDateTime("CreatedAt")
        }
    }
    $reader.Close()

    Write-Host "📋 找到需要迁移的会话数: $($sessions.Count)" -ForegroundColor Yellow

    $successCount = 0
    $skipCount = 0
    $errorCount = 0

    foreach ($session in $sessions) {
        Write-Host "`n处理会话: $($session.SessionId)"
        Write-Host "工作区路径: $($session.WorkspacePath)"
        Write-Host "用户名: $($session.Username)"

        # 检查目录是否已经存在
        $checkQuery = "SELECT DirectoryId FROM WorkspaceDirectory WHERE OwnerUsername = @username AND DirectoryPath = @path"
        $checkCmd = New-Object MySql.Data.MySqlClient.MySqlCommand($checkQuery, $connection)
        $checkCmd.Parameters.AddWithValue("@username", $session.Username) | Out-Null
        $checkCmd.Parameters.AddWithValue("@path", $session.WorkspacePath) | Out-Null
        $existingId = $checkCmd.ExecuteScalar()

        if ($existingId) {
            Write-Host "ℹ️  目录已存在，跳过创建: DirectoryId = $existingId" -ForegroundColor Cyan
            $directoryId = $existingId
            $skipCount++
        }
        else {
            # 创建目录记录
            $directoryName = [System.IO.Path]::GetFileName($session.WorkspacePath)
            if (-not $directoryName) {
                $directoryName = "会话工作区"
            }

            $insertQuery = @"
INSERT INTO WorkspaceDirectory
(DirectoryPath, DirectoryName, DirectoryType, OwnerUsername, CreatedAt, UpdatedAt)
VALUES (@path, @name, 'Custom', @username, @createdAt, @createdAt)
"@
            $insertCmd = New-Object MySql.Data.MySqlClient.MySqlCommand($insertQuery, $connection)
            $insertCmd.Parameters.AddWithValue("@path", $session.WorkspacePath) | Out-Null
            $insertCmd.Parameters.AddWithValue("@name", $directoryName) | Out-Null
            $insertCmd.Parameters.AddWithValue("@username", $session.Username) | Out-Null
            $insertCmd.Parameters.AddWithValue("@createdAt", $session.CreatedAt) | Out-Null

            if (-not $DryRun) {
                $rowsAffected = $insertCmd.ExecuteNonQuery()
                $directoryId = $insertCmd.LastInsertedId
                Write-Host "✅ 创建目录成功: DirectoryId = $directoryId" -ForegroundColor Green
                $successCount++
            }
            else {
                Write-Host "🔍 模拟执行：将创建目录 '$directoryName'" -ForegroundColor Yellow
                $directoryId = 0
            }
        }

        # 关联会话和目录
        if ($directoryId -gt 0) {
            # 检查是否已经关联
            $checkMappingQuery = "SELECT MappingId FROM SessionWorkspaceMapping WHERE SessionId = @sessionId AND DirectoryId = @directoryId"
            $checkMappingCmd = New-Object MySql.Data.MySqlClient.MySqlCommand($checkMappingQuery, $connection)
            $checkMappingCmd.Parameters.AddWithValue("@sessionId", $session.SessionId) | Out-Null
            $checkMappingCmd.Parameters.AddWithValue("@directoryId", $directoryId) | Out-Null
            $existingMappingId = $checkMappingCmd.ExecuteScalar()

            if (-not $existingMappingId) {
                $insertMappingQuery = @"
INSERT INTO SessionWorkspaceMapping
(SessionId, DirectoryId, Username, IsActive, CreatedAt, UpdatedAt)
VALUES (@sessionId, @directoryId, @username, 1, @createdAt, @createdAt)
"@
                $insertMappingCmd = New-Object MySql.Data.MySqlClient.MySqlCommand($insertMappingQuery, $connection)
                $insertMappingCmd.Parameters.AddWithValue("@sessionId", $session.SessionId) | Out-Null
                $insertMappingCmd.Parameters.AddWithValue("@directoryId", $directoryId) | Out-Null
                $insertMappingCmd.Parameters.AddWithValue("@username", $session.Username) | Out-Null
                $insertMappingCmd.Parameters.AddWithValue("@createdAt", $session.CreatedAt) | Out-Null

                if (-not $DryRun) {
                    $insertMappingCmd.ExecuteNonQuery() | Out-Null

                    # 更新ChatSession表的目录字段
                    $updateSessionQuery = @"
UPDATE ChatSession
SET DirectoryId = @directoryId,
    DirectoryName = (SELECT DirectoryName FROM WorkspaceDirectory WHERE DirectoryId = @directoryId),
    DirectoryType = (SELECT DirectoryType FROM WorkspaceDirectory WHERE DirectoryId = @directoryId),
    DirectoryOwner = (SELECT OwnerUsername FROM WorkspaceDirectory WHERE DirectoryId = @directoryId)
WHERE SessionId = @sessionId
"@
                    $updateSessionCmd = New-Object MySql.Data.MySqlClient.MySqlCommand($updateSessionQuery, $connection)
                    $updateSessionCmd.Parameters.AddWithValue("@directoryId", $directoryId) | Out-Null
                    $updateSessionCmd.Parameters.AddWithValue("@sessionId", $session.SessionId) | Out-Null
                    $updateSessionCmd.ExecuteNonQuery() | Out-Null

                    Write-Host "✅ 会话与目录关联成功" -ForegroundColor Green
                }
                else {
                    Write-Host "🔍 模拟执行：将会话关联到目录 $directoryId" -ForegroundColor Yellow
                }
            }
            else {
                Write-Host "ℹ️  会话已关联到此目录，跳过" -ForegroundColor Cyan
            }
        }
    }

    Write-Host "`n=== 迁移完成 ===" -ForegroundColor Cyan
    Write-Host "✅ 成功创建目录数: $successCount" -ForegroundColor Green
    Write-Host "ℹ️  跳过目录数: $skipCount" -ForegroundColor Cyan
    Write-Host "❌ 错误数: $errorCount" -ForegroundColor Red

    if ($DryRun) {
        Write-Host "`n⚠️  这是模拟执行，没有实际修改数据。去掉 -DryRun 参数执行实际迁移。" -ForegroundColor Yellow
    }
}
catch {
    Write-Host "`n❌ 迁移过程出错: $_" -ForegroundColor Red
}
finally {
    $connection.Close()
    Write-Host "`n🔌 数据库连接已关闭"
}
