#Make sure you are signed in:
az login

serviceName='my-fhir-service'
#Register the API application registration
apiapp=$(./create-aad-api-application-registration.sh --service-name $serviceName)


apiappid=$(echo $apiapp | jq -r .AppId)
clientName='my-fhir-client'
#Register the client application (e.g. for Postman):
clientapp=$(./create-aad-client-application-registration.sh -a $apiappid -d $clientName -r https://www.getpostman.com/oauth2/callback -i https://$clientName)

#Capture information:

authenticationAuthority=$(echo $apiapp | jq -r .Authority)
authenticationAudience=$(echo $apiapp | jq -r .Audience)
clientid=$(echo $clientapp | jq -r .AppId )
clientsecret=$(echo $clientapp | jq -r .AppSecret)
#Display the client app information:
echo $clientapp

rg='my-fhir-rg'
location='westus2'

#Create resource group
az group create --name $rg --location $location

templateUri='https://raw.githubusercontent.com/Microsoft/fhir-server/master/samples/templates/default-azuredeploy.json'

#Deploy FHIR server
az group deployment create -g $rg --template-uri $templateUri --parameters serviceName=$serviceName securityAuthenticationAuthority=${authenticationAuthority} securityAuthenticationAudience=${authenticationAudience}

accessUrl="$authenticationAuthority/oauth2/token"

accessTokenResult=$(curl -d "grant_type=client_credentials&client_id=$clientid&client_secret=$clientsecret&resource=$authenticationAudience" -H "Content-Type: application/x-www-form-urlencoded" -X POST $accessUrl)
accessToken=$(echo $accessTokenResult | jq -r .access_token)

# confirm that there's an access token available.
echo $accessToken

fhirServiceUrl="https://$serviceName.azurewebsites.net"

curl -X GET "$fhirServiceUrl/metadata"

appInsights=$(az monitor app-insights component create --app "$serviceName-app-insights" -l $location --kind web -g $rg --application-type web)
appInsightsKey=$(echo $appInsights | jq -r .instrumentationKey)

# is there a default azure app insights?  Looks like it's available in ARM but not enabled?
az webapp config appsettings set --settings ApplicationInsights:InstrumentationKey=$appInsightsKey -n $serviceName -g $rg
az webapp config appsettings set --settings APPINSIGHTS_INSTRUMENTATIONKEY=$appInsightsKey -n $serviceName -g $rg
az webapp config appsettings set --settings APPLICATIONINSIGHTS_CONNECTION_STRING="InstrumentationKey=$appInsightsKey" -n $serviceName -g $rg
az webapp config appsettings set --settings ApplicationInsightsAgent_EXTENSION_VERSION="~2" -n $serviceName -g $rg

az webapp show -n $serviceName -g $rg

# try with unsecure first.  
az webapp config appsettings set --settings FhirServer:Security:Enabled="True" -n $serviceName -g $rg

curl -X GET "https://$serviceName.azurewebsites.net/metadata"

# TODO: revisit with auth token.
curl -X POST -H "Content-Type: application/json" -H "Authorization: Bearer $accessToken" -d @sample-patient.json "https://$serviceName.azurewebsites.net/Patient"
