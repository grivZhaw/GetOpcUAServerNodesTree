# GetOpcUaServerNodesTree

Simple UA client that browse a server and return a json file containing the Nodes tree structure.

## Command Line arguments

Endpoint of the server: (mandatory)

```
-u opc.tcp://x.x.x.x:port
```

Filename where the nodes tree is saved: (default: NodesTree)

```
-n filename
```

Example of usage:
```
dotnet GetOpcUaServerNodesTree.dll -u opc.tcp://192.168.12.21:4840 -n tree
```