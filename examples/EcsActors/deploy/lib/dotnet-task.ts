import { Duration, RemovalPolicy } from "aws-cdk-lib";
import { ISecurityGroup, Peer, Port, SecurityGroup, SubnetType } from "aws-cdk-lib/aws-ec2";
import { Repository } from "aws-cdk-lib/aws-ecr";
import { DockerImageAsset, Platform } from "aws-cdk-lib/aws-ecr-assets";
import { AwsLogDriver, Compatibility, ContainerImage, CpuArchitecture, DeploymentControllerType, FargateService, ICluster, OperatingSystemFamily, TaskDefinition } from "aws-cdk-lib/aws-ecs";
import { IRole, Role, ServicePrincipal } from "aws-cdk-lib/aws-iam";
import { Code, Function } from "aws-cdk-lib/aws-lambda";
import { DockerImageName, ECRDeployment } from "cdk-ecr-deployment";
import { Construct } from "constructs";
import { CloudMesh } from "./cloudmesh";
import { dotNetArch } from "./dotnet-lambda";
import * as fs from 'fs';
import { execSync } from "child_process";
import { DnsRecordType } from "aws-cdk-lib/aws-servicediscovery";
import { DH_NOT_SUITABLE_GENERATOR } from "constants";

export class dotNetTaskCode {
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

    public static buildProject(path: string, handler?: string, arch?: dotNetArch): dotNetTaskCode {
        arch = arch || 'arm64';
        
        let projectName = '';
        let dockerFile = '';

        fs.readdirSync(path).forEach(file => {
            if (file.endsWith(".csproj")) {
                projectName = file;
            }
            if (file == 'Dockerfile') {
                dockerFile = file;
            }
        });

        if (projectName == '')
            throw 'Could not find a c# project file under path: ' + path;
        if (dockerFile == '')
            throw 'Could not find a Dockerfile in path: ' + path;
            
        projectName = projectName.substring(0, projectName.length - 7); // Strip away extension
        handler = handler || projectName;

        var outputPath = `../assets/${projectName}/`;

        const cmdLine = `dotnet publish -c Release -r linux-${arch} -o "${outputPath}publish/" --self-contained true -p:PublishReadyToRun=true -p:PublishReadyToRunShowWarnings=true -p:RuntimeIdentifiers=linux-${arch} ${path}`;
        console.log(`Building ${projectName}...\n${cmdLine}`);

        // Build project to asset folder /project-name/publish/
        execSync(cmdLine, {
            stdio: 'pipe'
        });

        // Copy Dockerfile to asset folder /project-name/
        fs.copyFileSync(`${path}/${dockerFile}`, `${outputPath}/${dockerFile}`);

        return new dotNetTaskCode(outputPath, projectName, handler, arch)
    }
}

export interface DotNetTaskProps {
    description: string;
    repositoryName: string;
    code: dotNetTaskCode;
    environment?: { [key: string]: string };
    ecsClusterExecutionRole: IRole;
    cpu?: number;
    memoryMiB?: number;
    ecsCluster: ICluster;
    cloudMesh: CloudMesh;
    desiredCount?: number;
    minHealthyPercent?: number;
    maxHealthyPercent?: number;
    securityGroups?: ISecurityGroup[];
    serviceInterfaceTypes?: string[];
    actorInterfaceTypes?: string[];
}

export class DotNetTask extends Construct {
    public role: Role;
    public handler: Function;

    constructor(scope: Construct, id: string, props: DotNetTaskProps) {
        super(scope, id);

        this.role = new Role(this, 'Role', {
            assumedBy: new ServicePrincipal('ecs-tasks.amazonaws.com')
        });

        const repo = new Repository(this, 'Repository', {
            repositoryName: props.repositoryName,
            removalPolicy: RemovalPolicy.DESTROY,            
            imageScanOnPush: true,
            lifecycleRules: [
                {
                    description: 'Keep 10 latest',
                    maxImageCount: 10
                }
            ]
        });

        repo.grantPull(props.ecsClusterExecutionRole);

        const dockerImage = new DockerImageAsset(this, 'Image', {
            platform: props.code.architecture == 'arm64' ? Platform.LINUX_ARM64 : Platform.LINUX_AMD64,
            directory: props.code.assetPath,
            exclude: [
                '.vs/',
                '**/bin/',
                '**/obj/'
            ]
        });

        const tag = `${Date.now().toString(16)}-${props.code.architecture == 'arm64' ? 'arm64' : 'amd64'}`;

        const deployment = new ECRDeployment(this, 'Deployment', {
            src: new DockerImageName(dockerImage.imageUri),
            dest: new DockerImageName(`${repo.repositoryUri}:${tag}`)
        });

        const task = new TaskDefinition(this, 'Task', {
            taskRole: this.role,
            executionRole: props.ecsClusterExecutionRole,
            compatibility: Compatibility.EC2_AND_FARGATE,
            runtimePlatform: {
                cpuArchitecture: props.code.architecture == 'arm64' ? CpuArchitecture.ARM64 : CpuArchitecture.X86_64,
                operatingSystemFamily: OperatingSystemFamily.LINUX
            },
            cpu: (props.cpu || 256).toString(),
            memoryMiB: (props.memoryMiB || 512).toString()            
        });

        task.node.addDependency(deployment);

        let environment = props.environment || {};
        environment = {
            cloudMapNamespace: props.cloudMesh.namespace.namespaceName
        };

        const container = task.addContainer('Container', {
            image: ContainerImage.fromEcrRepository(repo, tag),
            logging: new AwsLogDriver({
                streamPrefix: id,
            }),
            environment,
            dockerLabels: {
                app: id
            },
            portMappings: [
                { containerPort: 3500 }
            ],
            essential: true,
            stopTimeout: Duration.minutes(2),
            cpu: props.cpu || 256,
            memoryReservationMiB: props.memoryMiB || 512,            
        });

        const subnets = props.ecsCluster.vpc.selectSubnets({
            subnetType: SubnetType.PRIVATE_WITH_NAT
        });

        let securityGroups = props.securityGroups;
        if (!securityGroups) {
            const sg = new SecurityGroup(this, 'SecurityGroup', {
                vpc: props.ecsCluster.vpc,
                allowAllOutbound: true,
                description: `Default security group for ECS task ${this.node.path}`
            });
            sg.addIngressRule(Peer.anyIpv4(), Port.tcp(3500), 'Default CloudMesh port');
            securityGroups = [sg];
        }

        const service = new FargateService(this, id, {
            cluster: props.ecsCluster,
            desiredCount: props.desiredCount,
            taskDefinition: task,
            cloudMapOptions: {
                name: id, // CloudMap service name
                cloudMapNamespace: props.cloudMesh.namespace,
                dnsRecordType: DnsRecordType.SRV,
                container,
                containerPort: 3500
            },
            minHealthyPercent: props.minHealthyPercent,
            maxHealthyPercent: props.maxHealthyPercent,
            deploymentController: {
                type: DeploymentControllerType.ECS
            },
            securityGroups: securityGroups,
            assignPublicIp: false,
            vpcSubnets: subnets,
            circuitBreaker: {
                rollback: true
            }
        });

        if (props.serviceInterfaceTypes && props.serviceInterfaceTypes.length > 0) {
            props.cloudMesh.addEcsServices(id, props.serviceInterfaceTypes);
        }
        if (props.actorInterfaceTypes && props.actorInterfaceTypes.length > 0) {
            props.cloudMesh.addEcsActors(id, props.actorInterfaceTypes);
        }
    }
}