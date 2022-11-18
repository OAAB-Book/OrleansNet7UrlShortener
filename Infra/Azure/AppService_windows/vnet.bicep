param webAppName string
param location string = resourceGroup().location
param subnetName string = 'webApp-inner'

resource virtualNetwork 'Microsoft.Network/virtualNetworks@2022-05-01' = {
  name: '${webAppName}-vnet'
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.1.0.0/16'
      ]
    }
    subnets: [
      {
        name: subnetName
        properties: {
          addressPrefix: '10.1.0.0/26'
          delegations: [
            {
              name: 'webApp-delegation'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
    ]
  }
}

var subNetId = resourceId('Microsoft.Network/virtualNetworks/subnets', '${webAppName}-vnet', subnetName)

output vnetSubNetId string = subNetId
