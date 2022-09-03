import { IVpc, SubnetType } from "aws-cdk-lib/aws-ec2";
import { Effect, Policy, PolicyDocument, PolicyStatement, Role, ServicePrincipal } from "aws-cdk-lib/aws-iam";
import { Architecture, Code, Function, Runtime } from "aws-cdk-lib/aws-lambda";
import { execSync } from "child_process";
import { Construct } from "constructs";
import * as fs from 'fs';

export type dotNetArch = 'x64' | 'arm64';

export class dotNetCode {
    public assetPath: string;
    public projectName: string;
    public handler: string;
    public architecture: dotNetArch;

    constructor(assetPath: string, projectName: string, handler: string, arch: dotNetArch) {
        this.assetPath = assetPath;
        this.projectName = projectName;
        this.handler = handler;
        this.architecture = arch;
    }

    public static buildProject(path: string, handler?: string, arch?: dotNetArch): dotNetCode {
        arch = arch || 'arm64';

        let projectName = '';

        fs.readdirSync(path).forEach(file => {
            if (file.endsWith(".csproj")) {
                projectName = file;
            }
        });

        if (projectName == '')
            throw 'Could not find a c# project file under path: ' + path;
            
        projectName = projectName.substring(0, projectName.length - 7); // Strip away extension
        handler = handler || projectName;

        var outputPath = `../assets/${projectName}/`;
``
        const cmdLine = `dotnet publish -c Release -r linux-${arch} -o "${outputPath}" --self-contained true -p:PublishReadyToRun=true -p:PublishReadyToRunShowWarnings=true -p:RuntimeIdentifiers=linux-${arch} ${path}`;
        console.log(`Building ${projectName}\n${cmdLine}`);
        execSync(cmdLine, {
            stdio: 'pipe'
        });

        return new dotNetCode(outputPath, projectName, handler, arch)
    }
}

export interface DotNetLambdaProps {
    description: string;
    code: dotNetCode;
    environment?: { [key: string]: string };
    vpc?: IVpc;
}

export class DotNetLambda extends Construct {
    public role: Role;
    public handler: Function;

    constructor(scope: Construct, id: string, props: DotNetLambdaProps) {
        super(scope, id);

        this.role = new Role(this, 'Role', {
            assumedBy: new ServicePrincipal('lambda.amazonaws.com'),
            inlinePolicies: {
                logging: new PolicyDocument({
                    statements: [
                        new PolicyStatement({
                            effect: Effect.ALLOW,
                            resources: [ '*' ],
                            actions: [ 'logs:*' ]
                        })
                    ]
                })
            }
        });

          
        if (props.vpc) {
            this.role.addToPolicy(new PolicyStatement({
                effect: Effect.ALLOW,
                resources: ['*'],
                actions: [
                    'ec2:DescribeNetworkInterfaces',
                    'ec2:CreateNetworkInterface',
                    'ec2:DeleteNetworkInterface'
                ]
            }));
        }

        this.handler = new Function(this, 'Handler', {
            code: Code.fromAsset(props.code.assetPath),
            runtime: Runtime.PROVIDED_AL2,
            handler: props.code.handler,
            description: props.description,
            architecture: props.code.architecture == 'arm64' ? Architecture.ARM_64 : Architecture.X86_64,
            role: this.role,
            environment: props.environment,
            vpc: props.vpc,
            vpcSubnets: props.vpc ? props.vpc.selectSubnets({subnetType: SubnetType.PRIVATE_WITH_NAT}) : undefined
        });
    }
}