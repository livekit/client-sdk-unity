import os
import urllib.request
import urllib.parse
import configparser
import zipfile

# prepare progressbar
def show_progress(block_num, block_size, total_size):
    print(round(block_num * block_size / total_size *100,2), end="\r")

platforms = ['android', 'ios', 'macos', 'linux', 'windows']
download_dir = 'downloads~'

def main():
    config = configparser.ConfigParser()
    config.read('version.ini')
    tag_path = urllib.parse.quote(config['ffi']['tag'], safe='')

    for platform in platforms:
        archs = ['arm64', 'x86_64']
        if platform == 'android':
            archs = [ 'armv7', 'arm64', 'x86_64']
        elif platform == 'ios':
            archs = ['arm64', 'sim-arm64']
        elif platform == 'linux':
            archs = ['x86_64']

        for arch in archs:
            filename = 'ffi-' + platform + '-' + arch + '.zip'
            url = f"{config['ffi']['url']}/{tag_path}/{filename}"
            file_to_download = download_dir + '/' + filename
            if download_file_if_not_exists(url, file_to_download) :
                dest = 'Runtime/Plugins' + '/ffi-' + platform + '-' + arch
                unzip_file(file_to_download, filename, dest)

def download_file_if_not_exists(url, filename):
    if os.path.isdir(download_dir) == False:
        print( download_dir + " directory not exists, creating one...")
        os.makedirs(download_dir)
    if os.path.isfile(filename):
        print("file " + filename + " exists, skipping download")
        return False
    print("downloading file " + url)
    urllib.request.urlretrieve(url, filename, show_progress)
    return True

def unzip_file(srcfile, filename, dest):
    print("unzipping " + filename + " to " + dest)
    with zipfile.ZipFile(srcfile, 'r') as zip_ref:
        zip_ref.extractall(dest)
    print("unzipped " + srcfile)

if __name__ == "__main__":
    main()