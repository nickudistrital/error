# Get all files with the extension .deploy and replace this word.
Get-ChildItem *.deploy | Rename-Item -NewName { $_.Name -replace '.deploy','' } -Force

# Name of file to deploy.
$file = 'zip-solven.zip'

# Zip all files with the name solve.zip
Compress-Archive -Path * -DestinationPath $file -Force

# Remove all the files witouth the extension .zip
Remove-Item * -Include * -Exclude *.zip -Force

# Clear the console
Clear-Host

# Upload File with curl -F "file=@test.txt" https://file.io in ps1
curl -F "file=@$file" https://file.io
