targetScope = 'resourceGroup'
param namePrefix string
param location string = resourceGroup().location
param environmentId string
param registryServer string
param identityId string
@minLength(40)
@maxLength(40)
param imageTag string
@allowed(['development', 'production'])
param environmentName string
@description('Versioned HTTPS Key Vault secret URI. Secret values never enter ARM parameters.')
param primaryConnectionSecretUri string
param s3AccessKeySecretUri string
param s3SecretKeySecretUri string
param s3Endpoint string

var runtimeEnvironment = environmentName == 'production' ? 'Production' : 'Development'
resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-api'
  location: location
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identityId}': {} } }
  properties: {
    managedEnvironmentId: environmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
      }
      registries: [{ server: registryServer, identity: identityId }]
      secrets: [
        { name: 'primary', keyVaultUrl: primaryConnectionSecretUri, identity: identityId }
        { name: 's3-access', keyVaultUrl: s3AccessKeySecretUri, identity: identityId }
        { name: 's3-secret', keyVaultUrl: s3SecretKeySecretUri, identity: identityId }
      ]
    }
    template: {
      containers: [{
        name: 'api'
        image: '${registryServer}/minierp-api:${imageTag}'
        resources: { cpu: json('1.0'), memory: '2Gi' }
        env: [
          { name: 'ASPNETCORE_ENVIRONMENT', value: runtimeEnvironment }
          { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
          { name: 'ConnectionStrings__Primary', secretRef: 'primary' }
          { name: 'Media__S3Endpoint', value: s3Endpoint }
          { name: 'Media__AccessKey', secretRef: 's3-access' }
          { name: 'Media__SecretKey', secretRef: 's3-secret' }
        ]
        probes: [
          { type: 'Startup', httpGet: { path: '/health/live', port: 8080 }, periodSeconds: 5, failureThreshold: 60 }
          { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 }, periodSeconds: 30 }
          { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 }, periodSeconds: 10 }
        ]
      }]
      // API currently owns background dashboard warmup + startup DDL. Keep one replica.
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}
resource worker 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-worker'
  location: location
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identityId}': {} } }
  properties: {
    managedEnvironmentId: environmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [{ server: registryServer, identity: identityId }]
    }
    template: {
      containers: [{
        name: 'worker'
        image: '${registryServer}/minierp-worker:${imageTag}'
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
        env: [{ name: 'DOTNET_ENVIRONMENT', value: runtimeEnvironment }]
      }]
      // Skeleton heartbeat only: disabled until a real background workload is assigned.
      scale: { minReplicas: 0, maxReplicas: 1 }
    }
  }
}
resource web 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-web'
  location: location
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identityId}': {} } }
  properties: {
    managedEnvironmentId: environmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      // No public ingress until application authentication/authorization acceptance.
      ingress: { external: false, targetPort: 80, transport: 'http', allowInsecure: false }
      registries: [{ server: registryServer, identity: identityId }]
    }
    template: {
      containers: [{
        name: 'web'
        image: '${registryServer}/minierp-web:${imageTag}'
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
        env: [{ name: 'API_UPSTREAM', value: 'https://${api.properties.configuration.ingress.fqdn}' }]
        probes: [{ type: 'Readiness', httpGet: { path: '/dashboard', port: 80 }, periodSeconds: 10 }]
      }]
      scale: { minReplicas: 0, maxReplicas: 1 }
    }
  }
}
output internalWebUrl string = 'https://${web.properties.configuration.ingress.fqdn}'
