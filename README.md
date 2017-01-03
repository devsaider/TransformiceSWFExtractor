# TransformiceSWFExtractor
Extracts useful information (version, connection key..) from browser game called Transformice (www.transformice.com).

## Getting Started

You can intitialize **TransformiceSWF** object from either url or byte[]:
```c#
new TransformiceSWF(); // will download fresh swf from transformice.com/Transformice.swf
new TransformiceSWF(File.ReadAllBytes("../path/to/Transformice.swf"));
...
swf.parseData();
```

After calling parseData() you can access theese parameters:

* swf
 * version
 * connectionKey
 * xorKey
 * securityIntKey
 
## Perfomance

In current state this tool takes about 6s to find values on my high-end PC (i5-4460, SSD). You can improve perfomance by breaking main searching [loop](https://github.com/devsaider/TransformiceSWFExtractor/blob/master/TransformiceSWFExtractor.cs#L249) earlier.

## Dependencies

* [liblzma](https://github.com/D-Programming-Deimos/liblzma) and swfbinexport from [CyberShadow/RABCDAsm](https://github.com/CyberShadow/RABCDAsm)
* swfdump from [matthiaskramm/swftools](https://github.com/matthiaskramm/swftools)


## Authors

* **Ruslan Devsaider** - *Initial work* - [devsaider](https://github.com/devsaider)

See also the list of [contributors](https://github.com/devsaider/TransformiceSWFExtractor/contributors) who participated in this project.

## License

This project is licensed under the GNU GPL License - see the [LICENSE](LICENSE) file for details
