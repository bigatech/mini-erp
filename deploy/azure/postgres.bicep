// Optional managed data-plane foundation. Not executed by deploy.yml.
targetScope = 'resourceGroup'
param serverName string
param location string = resourceGroup().location
param administratorLogin string
@secure()
param administratorPassword string
param databaseName string = 'minierp'
resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: serverName
  location: location
  sku: { name: 'Standard_B1ms', tier: 'Burstable' }
  properties: {
    version: '17'
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorPassword
    storage: { storageSizeGB: 32 }
    backup: { backupRetentionDays: 7, geoRedundantBackup: 'Disabled' }
    highAvailability: { mode: 'Disabled' }
    network: { publicNetworkAccess: 'Enabled' }
  }
}
// No firewall rules: deny access until explicit outbound IP allowlist/private network is configured.
resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: server
  name: databaseName
  properties: { charset: 'UTF8', collation: 'en_US.utf8' }
}
output serverFqdn string = server.properties.fullyQualifiedDomainName
