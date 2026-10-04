PharmaX is installed. There is one more step, and PharmaX will walk you
through it the first time it starts.

WHAT IT WILL ASK

  What is this computer?

    One computer on its own
        PharmaX keeps its own file on this machine. Nothing else to set
        up. Choose this if the pharmacy has a single PC.

    The pharmacy's main computer
        This machine holds the database the tills share. Choose this on
        the pharmacist's PC. If "This is the pharmacy's main computer"
        was ticked in the installer, the database is already installed
        and its details are filled in: just name the pharmacy. When you
        finish, PharmaX shows the details every till needs - write them
        down.

    A till
        This machine uses the main computer's database. Choose this on
        each cashier PC, and enter the details the main computer showed.

  Then the database details, the till number, and the pharmacy's name
  and currency.

  Press "Test connection" before finishing. PharmaX will tell you
  exactly what is wrong if it cannot reach the server, and it will
  create the database for you if it is not there yet.

IN WHAT ORDER

  Set up the main computer first. The tills need a database to connect
  to, and the main computer is what creates it.

EVERY TILL NEEDS ITS OWN TILL NUMBER

  PharmaX gives each machine its own identity the first time it runs,
  and that identity is what keeps receipt numbers from colliding. Do not
  copy PharmaX's settings from one machine to another - run the setup on
  each one.

IF THE TILLS CANNOT SEE THE MAIN COMPUTER

  The two usual causes, in order:

    - The main computer's firewall is blocking the database port
      (3306 by default). Allow it for the private network.

    - The database account is granted at 'localhost' only, so the server
      accepts the main computer and refuses the tills. It needs to be
      granted at '%'. PharmaX says so explicitly when this happens.

WHERE THINGS ARE KEPT

  C:\ProgramData\PharmaX\workstation.json   this machine's settings
  C:\ProgramData\PharmaX\logs               logs, one file per day
  C:\ProgramData\PharmaX\attachments        scanned prescriptions
  C:\ProgramData\PharmaX\branding           the pharmacy's logo

  On the main computer, when the installer set up the database:

  C:\ProgramData\PharmaX\server.json        the tills' database login
  C:\ProgramData\PharmaX\database-admin.txt the MariaDB root password
                                            (administrators only)

  None of this is removed by an uninstall, so reinstalling or upgrading
  does not lose anything.
