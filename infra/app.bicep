// The app's own resources in rg-kbo, deployed by this repository's CI (platform-deploy.yml) with the
// platform's registry module. Names are derived from the slug, so existing resources are adopted in place.
// Mounts, auth, DNS, AI and ceilings are defined by the platform (co-creative/platform, apps/kbo).
//
// Operator upload to a share (Entra login, no keys):
//   az storage file upload --auth-mode login --enable-file-backup-request-intent \
//     --account-name <storage account> --share-name <share> --path <path> --source <file>

module resources 'br:crcocreative.azurecr.io/bicep/app-resources:1.1.0' = {
  name: 'app-resources-module'
  params: {
    slug: 'kbo'
    fileShares: [
      { name: 'kbo', quotaGb: 20 }
    ]
  }
}

@description('Capability settings, applied to the components of the app by platform-deploy.yml.')
output env object = resources.outputs.env
