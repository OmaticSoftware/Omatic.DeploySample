# Omatic.DeploySample
BB CRM package and deploy sample implementation.  This Visual Studio solution serves as a reference for automating the packaging and deployment of BB CRM customizations.  

See [the wiki](https://github.com/OmaticSoftware/Omatic.DeploySample/wiki) for documentation on getting the sample project working and/or understanding how it works.

## Overview

The sample solution performs the following steps:
* On successful VS solution build, package all customization components into a single file.  Customization components include Visual Studio assemblies (assumed to include catalog specs and UI model components), HTML/CSS/JS files associated with UI models, database revision assemblies, SQL files, and CRM system role XML files.
* After unpacking the package file in a target deployment environment, by default an included Powershell script will perform the following:
  * Copy customization files to correct locations (customization and UI model assemblies -> vroot\bin\custom, HTML/CSS/JS -> vroot\browser\htmlforms\, supporting files -> bbappfx\MSBuild\Custom)
  * Run pre-customization revisions
  * Run LoadSpec against "Package Spec" XML files
  * Run post-customization revisions
  * Load system roles
  * Run SQL files
* Optionally, the included script supports the following:
  * Copy-only installs (i.e. don't run revisions, don't load customizations, don't load system roles, and don't run SQL).  This is useful when deploying to load-balanced web servers where customization loading only needs to take place on one server--the rest just need copies of the same files.
  * Database restore.  This is useful for setting up new environments, refreshing the DB in existing environments, and automated scenarios (continuous integration, automated testing) where a DB backup in a known state is used as a starting point.  The database restore performs the following steps:
    * DB restore to configured SQL data/log path
    * Re-encrypt DB master key 
    * Change DB owner
    * Set recovery model ("Simple" or "Full")
    * Reset databse logins
    * Run CRM product revisions if necessary (useful when restoring DB from source environment with lower patch level than target environment)
    * Add currently executing user as application admin
    * Configure report server
