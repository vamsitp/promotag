#### PUBLISH RELEASE TO **GITHUB**
> Zip content under `bin/debug/netcoreapp3.0` folder first
```batch
REM https://curl.haxx.se/docs/manual.html
REM https://developer.github.com/v3/repos/releases/#create-a-release
curl https://api.github.com/repos/vamsitp/promotagz/releases -d "{\"tag_name\": \"1.0.0\", \"target_commitish\": \"master\", \"name\": \"1.0.0\", \"body\": \"First release!\", \"draft\": false, \"prerelease\": false}" -u vamsitp -H "Content-Type:application/json" -H X-GitHub-OTP:

    SAMPLE RESPONSE:
    {
        "url": "https://api.github.com/repos/vamsitp/promotagz/releases/20599621",
        "assets_url": "https://api.github.com/repos/vamsitp/promotagz/releases/20599621/assets",
        "upload_url": "https://uploads.github.com/repos/vamsitp/promotagz/releases/20599621/assets{?name,label}",
        "html_url": "https://github.com/vamsitp/promotagz/releases/tag/1.0.0",
        "id": 20599621,
        "tag_name": "1.0.0",
        "target_commitish": "master",
        "name": "1.0.0",
        "draft": false,
        ...
        "body": "First release!"
    }

REM https://developer.github.com/v3/repos/releases/#upload-a-release-asset
curl https://uploads.github.com/repos/vamsitp/promotagz/releases/20599621/assets?name=PromoTagz_1.0.0.zip --data-binary "bin\Debug\netcoreapp3.0\PromoTagz_1.0.0.zip" -u vamsitp -H "Content-Type:application/octet-stream" -H X-GitHub-OTP:
    
    SAMPLE RESPONSE:
    {
        "url":"https://api.github.com/repos/vamsitp/promotagz/releases/assets/15396695",
        "id":15396695,
        "node_id":"MDEyOlJlbGVhc2VBc3NldDE1Mzk2Njk1",
        "name":"PromoTagz_1.0.0.zip",
        "label":"",
        "uploader":
        {
            ...
        },
        "content_type":"application/octet-stream",
        "state":"uploaded",
        "size":43,
        "download_count":0,
        "created_at":"2019-10-10T05:46:44Z",
        "updated_at":"2019-10-10T05:46:45Z",
        "browser_download_url":"https://github.com/vamsitp/promotagz/releases/download/1.0.0/PromoTagz_1.0.0.zip"
    }
```